using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

// Lists / reads individual .rpo/.rpoz meshes inside an APK/IPA zip by file stem, without decoding the whole
// archive. Defensive: malformed zip yields empty/null, never throws.
public static class RpoAssetLister
{
    private const long MaxEntryBytes = 50_000_000L;

    // The distinct file stems (no dir, no extension) of every .rpo/.rpoz entry in the archive, sorted.
    public static IReadOnlyList<string> ListStems(byte[] archiveZipBytes)
    {
        var stems = new SortedSet<string>(StringComparer.Ordinal);
        ForEachRpoEntry(archiveZipBytes, (stem, _) => { stems.Add(stem); return false; });
        return stems.ToList();
    }

    // The bytes of the first .rpo/.rpoz entry whose stem matches, or null. Tries the exact stem.
    public static byte[]? ReadStem(byte[] archiveZipBytes, string stem)
    {
        byte[]? found = null;
        ForEachRpoEntry(archiveZipBytes, (s, read) =>
        {
            if (!string.Equals(s, stem, StringComparison.Ordinal)) return false;
            found = read();
            return true; // stop
        });
        return found;
    }

    // Walks .rpo/.rpoz entries; visit returns true to stop. read() lazily materializes the entry bytes so
    // listing does not decompress every mesh.
    private static void ForEachRpoEntry(byte[] zipBytes, Func<string, Func<byte[]>, bool> visit)
    {
        if (zipBytes is null || zipBytes.Length == 0) return;
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read); }
        catch { return; }

        using (zip)
        {
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (!name.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;

                var stem = Stem(name);
                byte[] Read()
                {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    return buf.ToArray();
                }
                if (visit(stem, Read)) return;
            }
        }
    }

    private static string Stem(string fullName)
    {
        var slash = fullName.LastIndexOfAny(['/', '\\']);
        var name = slash >= 0 ? fullName[(slash + 1)..] : fullName;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
