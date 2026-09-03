using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public static class DeviceFileParser {
    public static bool IsDeviceFile(string fileName) =>
        fileName.Contains(".egidevice.", StringComparison.OrdinalIgnoreCase);

    public static ParsedDevice? Parse(string fileName, string content) {
        if (!IsDeviceFile(fileName)) return null;

        (string? filePlatform, int order) = SplitName(fileName);
        var kv = ParseDotenv(content);

        return new ParsedDevice(
            order,
            Nz(Get(kv, "Id")),
            Nz(Get(kv, "Platform")) ?? Nz(filePlatform),
            Nz(Get(kv, "Label")),
            Nz(Get(kv, "Target")),
            Nz(Get(kv, "Package")));
    }

    private static (string? Platform, int Order) SplitName(string fileName) {
        int idx = fileName.IndexOf(".egidevice.", StringComparison.OrdinalIgnoreCase);
        string? platform = idx > 0 ? fileName[..idx] : null;
        string suffix = fileName[(idx + ".egidevice.".Length)..];
        return (platform,
            int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue);
    }

    private static Dictionary<string, string> ParseDotenv(string content) {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in content.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            kv[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return kv;
    }

    private static string? Get(Dictionary<string, string> kv, string key) =>
        kv.GetValueOrDefault(key);

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public sealed record ParsedDevice(
        int Order,
        string? Id,
        string? Platform,
        string? Label,
        string? Target,
        string? Package);
}
