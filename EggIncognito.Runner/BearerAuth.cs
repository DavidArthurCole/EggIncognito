using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Runner;

public static class BearerAuth {
    private const string Prefix = "Bearer ";

    public static bool Matches(string? header, string secret) {
        if (string.IsNullOrEmpty(secret)) return false;
        if (header is null || !header.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        byte[] presented = Encoding.UTF8.GetBytes(header[Prefix.Length..]);
        byte[] expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
