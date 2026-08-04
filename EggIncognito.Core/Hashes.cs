using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Core;

public static class Hashes {
    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Sha256HexShort(string text, int length = 12) => Sha256Hex(text)[..length];

    public static string Sha256HexShort(byte[] bytes, int length = 12) => Sha256Hex(bytes)[..length];
}
