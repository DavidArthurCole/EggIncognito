using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services.DataApi;

public static class ApiKeyGen {
    public const string Scheme = "egi_live_";
    public const string SchemeName = "ApiKey";
    public const string Claim = "egi:apikey";

    public static (string Full, string Hash, string Prefix) Mint() {
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string body = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string full = Scheme + body;
        return (full, HashOf(full), full[..12]);
    }

    public static string HashOf(string full) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
}
