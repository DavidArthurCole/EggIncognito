using System.IO.Compression;

namespace EggIncognito.Core.Services.Devices;

public sealed record MagiskModule(string Id, string State) {
    public const string Enabled = "on";
    public const string Disabled = "disabled";
    public const string Removing = "removing";

    public bool Ok => State == Enabled;
    public bool Pending => State == Removing;
}

public static class MagiskModules {
    public const string ScanMarker = "egi-modscan-done";

    public const string ScanCommand =
        "for d in /data/adb/modules/*/; do [ -d \"$d\" ] || continue; "
        + "id=$(grep -m1 ^id= \"$d/module.prop\" 2>/dev/null | cut -d= -f2); "
        + "[ -n \"$id\" ] || id=${d%/}; id=${id##*/}; "
        + "s=on; [ -f \"$d/disable\" ] && s=disabled; [ -f \"$d/remove\" ] && s=removing; "
        + "echo \"mod $id $s\"; done; echo " + ScanMarker;

    public static bool Ran(string stdout) => stdout.Contains(ScanMarker, StringComparison.Ordinal);

    public static List<MagiskModule> Parse(string stdout) {
        var mods = new List<MagiskModule>();
        foreach (string line in stdout.Split('\n')) {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != "mod") continue;
            mods.Add(new MagiskModule(parts[1], parts[2]));
        }

        return mods;
    }

    public static List<MagiskModule> Live(IEnumerable<MagiskModule> mods) => [.. mods.Where(m => !m.Pending)];

    public static string Describe(IEnumerable<MagiskModule> mods) =>
        string.Join(", ", mods.Select(m => $"{m.Id} {m.State}"));

    public static string? IdFromZip(byte[] zip) {
        try {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            if (archive.GetEntry("module.prop") is not { } entry) return null;
            using var reader = new StreamReader(entry.Open());
            while (reader.ReadLine() is { } line) {
                if (line.StartsWith("id=", StringComparison.Ordinal)) return line["id=".Length..].Trim();
            }

            return null;
        } catch (InvalidDataException) {
            return null;
        }
    }
}
