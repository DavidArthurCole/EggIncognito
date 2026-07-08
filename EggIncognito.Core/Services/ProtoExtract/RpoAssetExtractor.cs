using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

// Pulls the ship meshes out of a game archive (Android APK or iOS IPA, both zips). The .rpo/.rpoz mesh files
// live in an `rpos/` directory inside the archive. Each is decoded to a .glb via RpoMeshDecoder. Size-capped,
// degrades on malformed entries instead of throwing.
public static class RpoAssetExtractor
{
    private const long MaxEntryBytes = 50_000_000L; // a single mesh is small; reject oversized entries

    // One decoded mesh: the key (base filename without extension, used as the .glb name) + the glb bytes +
    // the decode metadata the manifest needs. Failed decodes carry Ok=false and a diagnostic, no bytes.
    public sealed record Asset(string Key, string SourceEntry, RpoMeshDecoder.DecodeResult Decode);

    public sealed record ExtractResult(bool Ok, IReadOnlyList<Asset> Assets, string Diagnostics);

    public static ExtractResult Extract(byte[] archiveZipBytes)
    {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new ExtractResult(false, [], "empty archive");

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, writable: false), ZipArchiveMode.Read);
        }
        catch
        {
            // Not a zip: try the raw bytes as a single .rpo/.rpoz (a directly supplied mesh file).
            var single = RpoMeshDecoder.Decode(archiveZipBytes);
            return single.Ok
                ? new ExtractResult(true, [new Asset("mesh", "<raw>", single)], "ok")
                : new ExtractResult(false, [], "not a zip and not a decodable mesh");
        }

        var entries = new List<(string Name, byte[] Bytes)>();
        using (zip)
        {
            foreach (var entry in zip.Entries)
            {
                if (!IsRpoEntry(entry.FullName)) continue;
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;
                try
                {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    entries.Add((entry.FullName, buf.ToArray()));
                }
                catch { /* skip unreadable entry */ }
            }
        }
        return DecodeEntries(entries);
    }

    // Decodes a flat list of (path, bytes) mesh entries already pulled from a zip, tar, or directory. Filters
    // to .rpo/.rpoz, decodes each, keys by base filename.
    public static ExtractResult FromEntries(IEnumerable<(string Name, byte[] Bytes)> entries) =>
        DecodeEntries(entries.Where(e => IsRpoEntry(e.Name) && e.Bytes.LongLength is > 0 and <= MaxEntryBytes));

    private static ExtractResult DecodeEntries(IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        var assets = new List<Asset>();
        foreach (var (name, bytes) in entries)
        {
            var key = KeyFromEntry(name);
            assets.Add(new Asset(key, name, RpoMeshDecoder.Decode(bytes, key)));
        }
        var ok = assets.Any(a => a.Decode.Ok);
        return new ExtractResult(ok, assets,
            ok ? "ok" : assets.Count == 0 ? "no .rpo/.rpoz entries found" : "found meshes but none decoded");
    }

    private static bool IsRpoEntry(string fullName) =>
        fullName.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
        || fullName.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase);

    // Base filename without directory or extension, e.g. "assets/rpos/henerprise.rpoz" -> "henerprise".
    // The caller maps this to the MissionInfo.Spaceship enum key when naming the output .glb.
    private static string KeyFromEntry(string fullName)
    {
        var slash = fullName.LastIndexOfAny(['/', '\\']);
        var name = slash >= 0 ? fullName[(slash + 1)..] : fullName;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
