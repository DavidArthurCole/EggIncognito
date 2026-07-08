using System.Security.Cryptography;

namespace EggIncognito.Core;

// ProtoHash computes the identity of the checked-in ei.proto snapshot, compared against an incoming
// event's protoSha to decide whether a new game version carries a proto change needing a manual refresh.
public static class ProtoHash
{
    // Candidate locations of ei.proto relative to a root, most-specific first. Either resolves to the
    // same frozen bytes, so the hash is build-stable.
    private static readonly string[] RelativeCandidates =
    [
        Path.Combine("Proto", "ei.proto"),
        Path.Combine("EggIncognito.Core", "Proto", "ei.proto"),
    ];

    // Lowercase hex SHA-256 of the checked-in ei.proto found under root or an ancestor. Throws if none found.
    public static string Current(string root)
    {
        var path = Locate(root) ?? throw new FileNotFoundException(
            $"ei.proto not found under '{root}' or its ancestors.");
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string? Locate(string root)
    {
        var dir = new DirectoryInfo(root);
        while (dir is not null)
        {
            foreach (var rel in RelativeCandidates)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
