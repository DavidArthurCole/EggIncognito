namespace EggIncognito.Core.Services.Devices;


public static class DeviceFileParser
{
    public sealed record ParsedDevice(
        int Order, string? Id, string? Platform, string? Label, string? Target, string? Package);

    public static bool IsDeviceFile(string fileName) =>
        fileName.Contains(".egidevice.", StringComparison.OrdinalIgnoreCase);

   
   
    public static ParsedDevice? Parse(string fileName, string content)
    {
        if (!IsDeviceFile(fileName)) return null;

        var (filePlatform, order) = SplitName(fileName);
        var kv = ParseDotenv(content);

        return new ParsedDevice(
            Order: order,
            Id: Nz(Get(kv, "Id")),
            Platform: Nz(Get(kv, "Platform")) ?? Nz(filePlatform),
            Label: Nz(Get(kv, "Label")),
            Target: Nz(Get(kv, "Target")),
            Package: Nz(Get(kv, "Package")));
    }

   
    private static (string? Platform, int Order) SplitName(string fileName)
    {
        var idx = fileName.IndexOf(".egidevice.", StringComparison.OrdinalIgnoreCase);
        var platform = idx > 0 ? fileName[..idx] : null;
        var suffix = fileName[(idx + ".egidevice.".Length)..];
        return (platform, int.TryParse(suffix, out var n) ? n : int.MaxValue);
    }

    private static Dictionary<string, string> ParseDotenv(string content)
    {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            kv[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return kv;
    }

    private static string? Get(Dictionary<string, string> kv, string key) => kv.TryGetValue(key, out var v) ? v : null;
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
