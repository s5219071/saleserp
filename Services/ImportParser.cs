using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using EcnesoftFieldSales.Domain;

namespace EcnesoftFieldSales.Services;

public interface IImportParser
{
    Task<IReadOnlyList<CustomerImportRow>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken);
}

public sealed class ImportParser : IImportParser
{
    public async Task<IReadOnlyList<CustomerImportRow>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var table = extension switch
        {
            ".csv" => await ReadCsvAsync(stream, cancellationToken),
            ".xlsx" => ReadXlsx(stream),
            ".xls" => throw new NotSupportedException("Legacy .xls is not supported. Save the sheet as .xlsx or CSV."),
            _ => throw new NotSupportedException("Only .csv and .xlsx imports are supported.")
        };

        return MapRows(table);
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = new List<IReadOnlyList<string>>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null && !string.IsNullOrWhiteSpace(line))
            {
                rows.Add(ParseCsvLine(line));
            }
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml") ??
                         archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));

        if (sheetEntry is null)
        {
            return [];
        }

        using var sheetStream = sheetEntry.Open();
        var document = XDocument.Load(sheetStream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<IReadOnlyList<string>>();

        foreach (var row in document.Descendants(spreadsheet + "row"))
        {
            var values = new Dictionary<int, string>();
            foreach (var cell in row.Elements(spreadsheet + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? "";
                var columnIndex = GetColumnIndex(reference);
                values[columnIndex] = GetCellValue(cell, sharedStrings, spreadsheet);
            }

            if (values.Count == 0)
            {
                continue;
            }

            var max = values.Keys.Max();
            var line = Enumerable.Range(0, max + 1).Select(index => values.GetValueOrDefault(index, "")).ToArray();
            if (line.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    private static IReadOnlyList<CustomerImportRow> MapRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count < 2)
        {
            return [];
        }

        var headers = rows[0]
            .Select((value, index) => new { Key = NormalizeHeader(value), Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.First().Index);

        var mapped = new List<CustomerImportRow>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var abn = Get(row, headers, "abn");
            if (string.IsNullOrWhiteSpace(abn))
            {
                continue;
            }

            mapped.Add(new CustomerImportRow
            {
                RowNumber = rowIndex + 1,
                ABN = abn,
                CompanyName = Get(row, headers, "companyname", "legalname", "businessname"),
                Address = Get(row, headers, "address", "streetaddress"),
                City = Get(row, headers, "city", "suburb"),
                State = Get(row, headers, "state"),
                Postcode = Get(row, headers, "postcode", "postalcode"),
                Phone = Get(row, headers, "phone", "mobile"),
                Email = Get(row, headers, "email"),
                Latitude = TryDouble(Get(row, headers, "latitude", "lat")),
                Longitude = TryDouble(Get(row, headers, "longitude", "lng", "lon")),
                CustomerType = Get(row, headers, "customertype", "type"),
                ProspectStatus = Get(row, headers, "prospectstatus", "status"),
                GroupId = TryInt(Get(row, headers, "groupid")),
                AssignedUserId = TryInt(Get(row, headers, "assigneduserid", "salesuserid"))
            });
        }

        return mapped;
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"' && inQuotes && index + 1 < line.Length && line[index + 1] == '"')
            {
                value.Append('"');
                index++;
                continue;
            }

            if (current == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                values.Add(value.ToString().Trim());
                value.Clear();
                continue;
            }

            value.Append(current);
        }

        values.Add(value.ToString().Trim());
        return values.ToArray();
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string GetCellValue(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace spreadsheet)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
        }

        var raw = cell.Element(spreadsheet + "v")?.Value ?? "";
        if (type == "s" && int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return raw;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var index = 0;
        foreach (var letter in letters)
        {
            index *= 26;
            index += letter - 'A' + 1;
        }

        return Math.Max(0, index - 1);
    }

    private static string NormalizeHeader(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headers, params string[] names)
    {
        foreach (var name in names.Select(NormalizeHeader))
        {
            if (headers.TryGetValue(name, out var index) && index >= 0 && index < row.Count)
            {
                var value = row[index]?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static double? TryDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static int? TryInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
