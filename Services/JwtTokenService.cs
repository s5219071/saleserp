using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EcnesoftFieldSales.Domain;
using Microsoft.Extensions.Options;

namespace EcnesoftFieldSales.Services;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "EcnesoftFieldSales";
    public string Audience { get; init; } = "EcnesoftSalesWeb";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 480;
}

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(UserAccount user);
    ClaimsPrincipal CreatePrincipal(UserAccount user);
    ClaimsPrincipal? ValidateToken(string token);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly byte[] _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);

        if (_signingKey.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key must be at least 32 bytes.");
        }
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(UserAccount user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email,
            ["name"] = user.FullName,
            ["role"] = user.Role.ToString(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signatureSegment = Sign($"{headerSegment}.{payloadSegment}");

        return ($"{headerSegment}.{payloadSegment}.{signatureSegment}", expiresAt);
    }

    public ClaimsPrincipal CreatePrincipal(UserAccount user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "EcnesoftAuth"));
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        var expectedSignature = Sign($"{segments[0]}.{segments[1]}");
        if (!FixedTimeEquals(expectedSignature, segments[2]))
        {
            return null;
        }

        Dictionary<string, JsonElement>? payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(segments[1]));
            payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
        }
        catch
        {
            return null;
        }

        if (payload is null ||
            !TryReadString(payload, "iss", out var issuer) ||
            !TryReadString(payload, "aud", out var audience) ||
            !TryReadString(payload, "sub", out var subject) ||
            !TryReadString(payload, "email", out var email) ||
            !TryReadString(payload, "name", out var name) ||
            !TryReadString(payload, "role", out var role) ||
            !payload.TryGetValue("exp", out var expElement) ||
            !expElement.TryGetInt64(out var exp))
        {
            return null;
        }

        if (issuer != _options.Issuer ||
            audience != _options.Audience ||
            DateTimeOffset.FromUnixTimeSeconds(exp) <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ManualJwt"));
    }

    private string Sign(string unsignedToken)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken)));
    }

    private static bool TryReadString(Dictionary<string, JsonElement> payload, string key, out string value)
    {
        value = "";
        if (!payload.TryGetValue(key, out var element))
        {
            return false;
        }

        value = element.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var incoming = value.Replace('-', '+').Replace('_', '/');
        var padding = incoming.Length % 4;
        if (padding > 0)
        {
            incoming = incoming.PadRight(incoming.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(incoming);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
