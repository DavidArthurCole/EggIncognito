using System.IO.Compression;

namespace EggIncognito.Core.Services.Devices;

public static class IntegrityBoxModule {
    private const string ActionEntry = "action.sh";
    private const string LegacyPropEntry = "toolkit/legacy.prop";
    private const string TeesimEntry = "webroot/common_scripts/teesim.sh";
    private const string PatchDateKey = "PATCH_DATE=";

    public static string? PatchDate(byte[] zip) {
        if (ReadEntry(zip, ActionEntry) is not { } text) return null;
        foreach (string raw in text.Split('\n')) {
            string line = raw.Trim();
            if (!line.StartsWith(PatchDateKey, StringComparison.Ordinal)) continue;
            string value = line[PatchDateKey.Length..].Trim().Trim('"');
            return value.Length > 0 ? value : null;
        }

        return null;
    }

    public static PifProfile? LegacyProfile(byte[] zip) =>
        ReadEntry(zip, LegacyPropEntry) is { } text ? PifProp.Parse(text) : null;

    public static string? TeesimSyncScript(byte[] zip) => ReadEntry(zip, TeesimEntry);

    private static string? ReadEntry(byte[] zip, string name) {
        try {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            if (archive.GetEntry(name) is not { } entry) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        } catch (Exception ex) when (ex is InvalidDataException or IOException) {
            return null;
        }
    }
}
