using System.Security.Cryptography;

namespace EggIncognito.Core;

public static class ProtoHash {


    private static readonly string[] RelativeCandidates =
    [
        Path.Combine("Proto", "ei.proto"),
        Path.Combine("EggIncognito.Core", "Proto", "ei.proto"),
    ];


    public static string Current(string root) {
        var path = Locate(root) ?? throw new FileNotFoundException(
            $"ei.proto not found under '{root}' or its ancestors.");
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string? Locate(string root) {
        var dir = new DirectoryInfo(root);
        while (dir is not null) {
            foreach (var rel in RelativeCandidates) {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
