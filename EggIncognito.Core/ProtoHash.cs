using System.Security.Cryptography;

namespace EggIncognito.Core;

// ProtoHash computes the identity of the checked-in ei.proto snapshot. The sync ingest handler
// compares an incoming event's protoSha against this to decide whether a new game version carries
// a proto change that needs a manual refresh.
public static class ProtoHash
{
    // Candidate locations of ei.proto relative to a root, most-specific first. Hosted publish copies
    // the proto next to the payload (Proto/ei.proto via CopyToOutputDirectory); the dev tree keeps it
    // in the Core project (EggIncognito.Core/Proto/ei.proto, reachable by walking up from the web
    // project's content root). Either resolves to the same frozen bytes, so the hash is build-stable.
    private static readonly string[] RelativeCandidates =
    [
        Path.Combine("Proto", "ei.proto"),
        Path.Combine("EggIncognito.Core", "Proto", "ei.proto"),
    ];

    // Current returns the lowercase hex SHA-256 of the checked-in ei.proto found under root, searching
    // root then each ancestor for the known relative layouts. Throws if no ei.proto is found.
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
