using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Core;

public static class ProtoHash {
    public static string Of(string protoText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protoText))).ToLowerInvariant();

    private static readonly string[] RelativeCandidates = [
        Path.Combine("Proto", "ei.proto"),
        Path.Combine("EggIncognito.Core", "Proto", "ei.proto")
    ];


    public static string Current(string root) {
        string path = Locate(root) ?? throw new FileNotFoundException(
            $"ei.proto not found under '{root}' or its ancestors.");
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string? Locate(string root) {
        var dir = new DirectoryInfo(root);
        while (dir is not null) {
            foreach (string rel in RelativeCandidates) {
                string candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
