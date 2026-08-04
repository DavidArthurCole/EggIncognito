using System.Security.Cryptography;
using EggIncognito.Core;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.DataApi;

public static class ApiKeyGen {
    public const string Scheme = "egi_live_";
    public const string SchemeName = "ApiKey";
    public const string Claim = AuthClaims.ApiKeyClaim;

    public static (string Full, string Hash, string Prefix) Mint() {
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string body = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string full = Scheme + body;
        return (full, HashOf(full), full[..12]);
    }

    public static string HashOf(string full) =>
        Hashes.Sha256Hex(full);
}
