using System.Text.Json;

namespace EcnesoftFieldSales.Services;

public sealed class RealAbrLookupClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RealAbrLookupClient> logger) : IAbrLookupClient
{
    private const string Endpoint = "https://abr.business.gov.au/json/AbnDetails.aspx";

    public async Task<AbrLookupResult> LookupAsync(string abn, CancellationToken cancellationToken)
    {
        var normalized = AbnValidator.Normalize(abn);
        if (normalized.Length != 11)
        {
            return new AbrLookupResult(normalized, false, null, null, null, null, null, "ABN must contain 11 digits.");
        }

        var apiKey = configuration["Abr:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Equals("YOUR_ABR_GUID", StringComparison.OrdinalIgnoreCase))
        {
            return new AbrLookupResult(
                normalized,
                false,
                null,
                null,
                null,
                null,
                null,
                "ABR API key is not configured.");
        }

        try
        {
            var requestUri = $"{Endpoint}?abn={Uri.EscapeDataString(normalized)}&guid={Uri.EscapeDataString(apiKey)}";
            var payload = await httpClient.GetStringAsync(requestUri, cancellationToken);
            using var document = JsonDocument.Parse(UnwrapJsonp(payload));
            var root = document.RootElement;

            var message = ReadString(root, "Message");
            var status = ReadString(root, "AbnStatus");
            var state = ReadString(root, "AddressState");
            var postcode = ReadString(root, "AddressPostcode");
            var address = FirstNonEmpty(
                ReadString(root, "Address"),
                string.Join(" ", new[] { state, postcode }.Where(value => !string.IsNullOrWhiteSpace(value))));
            var isActive = status?.Equals("Active", StringComparison.OrdinalIgnoreCase) == true;

            return new AbrLookupResult(
                normalized,
                isActive && string.IsNullOrWhiteSpace(message),
                ReadString(root, "EntityName"),
                ReadBusinessName(root),
                string.IsNullOrWhiteSpace(address) ? null : address,
                state,
                postcode,
                string.IsNullOrWhiteSpace(message)
                    ? $"ABR lookup completed. Status: {status ?? "unknown"}."
                    : message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ABR lookup failed for {ABN}", normalized);
            return new AbrLookupResult(
                normalized,
                false,
                null,
                null,
                null,
                null,
                null,
                "ABR lookup request failed.");
        }
    }

    private static string UnwrapJsonp(string payload)
    {
        var trimmed = payload.Trim();
        var open = trimmed.IndexOf('(');
        if (open > 0 && trimmed.EndsWith(");", StringComparison.Ordinal))
        {
            return trimmed[(open + 1)..^2];
        }

        if (open > 0 && trimmed.EndsWith(')'))
        {
            return trimmed[(open + 1)..^1];
        }

        return trimmed;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? ReadBusinessName(JsonElement root)
    {
        if (!root.TryGetProperty("BusinessName", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : ReadString(item, "organisationName") ?? ReadString(item, "name"))
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            _ => value.ToString()
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
