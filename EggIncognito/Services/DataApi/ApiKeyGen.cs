using System.Security.Cryptography;

namespace EggIncognito.Services.DataApi;

public static class ApiKeyGen
{
    public const string Scheme = "egi_live_";
    public const string SchemeName = "ApiKey";
    public const string Claim = "egi:apikey";

    public static (string Full, string Hash, string Prefix) Mint()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var full = Scheme + body;
        return (full, HashOf(full), full[..12]);
    }

    public static string HashOf(string full) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full))).ToLowerInvariant();
}
