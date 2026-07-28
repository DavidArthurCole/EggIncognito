using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Runner.Devices;

public static class RunnerDeviceSource {
    public static IReadOnlyList<DeviceFileParser.ParsedDevice> Read(string? dir) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return [];
        return [.. Directory.EnumerateFiles(dir)
            .Where(p => DeviceFileParser.IsDeviceFile(Path.GetFileName(p)))
            .Select(p => DeviceFileParser.Parse(Path.GetFileName(p), File.ReadAllText(p)))
            .OfType<DeviceFileParser.ParsedDevice>()
            .OrderBy(p => p.Order)];
    }
}
