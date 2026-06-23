using System.Text.RegularExpressions;

namespace EcnesoftFieldSales.Services;

public static class AbnValidator
{
    private static readonly int[] Weights = [10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19];

    public static string Normalize(string? abn) =>
        Regex.Replace(abn ?? "", "[^0-9]", "");

    public static bool IsValid(string? abn)
    {
        var digits = Normalize(abn);
        if (digits.Length != 11 || digits.Any(c => c < '0' || c > '9'))
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            var digit = digits[index] - '0';
            if (index == 0)
            {
                digit -= 1;
            }

            sum += digit * Weights[index];
        }

        return sum % 89 == 0;
    }
}

public sealed record AbrLookupResult(
    string ABN,
    bool IsValid,
    string? LegalName,
    string? TradingName,
    string? Address,
    string? State,
    string? Postcode,
    string Message);

public interface IAbrLookupClient
{
    Task<AbrLookupResult> LookupAsync(string abn, CancellationToken cancellationToken);
}

public sealed class MockAbrLookupClient(HttpClient httpClient) : IAbrLookupClient
{
    public async Task<AbrLookupResult> LookupAsync(string abn, CancellationToken cancellationToken)
    {
        var normalized = AbnValidator.Normalize(abn);

        // The injected HttpClient is intentional: replace this mock body with SendAsync
        // against ABR Lookup when the production API key and endpoint are available.
        _ = httpClient;
        await Task.Delay(35, cancellationToken);

        if (!AbnValidator.IsValid(normalized))
        {
            return new AbrLookupResult(
                normalized,
                false,
                null,
                null,
                null,
                null,
                null,
                "ABN checksum failed.");
        }

        var suffix = normalized[^3..];
        return new AbrLookupResult(
            normalized,
            true,
            $"ABR Verified Business {suffix} Pty Ltd",
            $"Verified Trading {suffix}",
            "Level 12, 680 George Street, Sydney",
            "NSW",
            "2000",
            "Mock ABR lookup succeeded.");
    }
}
