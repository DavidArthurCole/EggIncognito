using System.Globalization;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Fake;

public static class FakeDeviceOptions {
    public const string Section = "Devices:Fake";
    public const string IdPrefix = "fake-";
    public const string DefaultPackage = "com.auxbrain.egginc";
    public const string TargetPrefix = "fake:";
    private const string Marker = "fake";

    public static FakeDeviceSettings Bind(IConfiguration config) {
        var section = config.GetSection(Section);
        var declared = ReadDevices(section.GetSection("Devices"));
        return new FakeDeviceSettings(
            declared.Count > 0 ? declared : Defaults(),
            section.GetValue("SweepMinutes", 15),
            section.GetValue("SlowEntryMs", 45000),
            section.GetValue("WedgeBackdateMinutes", 35),
            section.GetValue("CaptureIntervalMs", 4000));
    }

    public static IReadOnlyList<DeviceEntry> Entries(FakeDeviceSettings settings) =>
        [.. settings.Devices.Select(d => new DeviceEntry(d.Id, d.Platform, d.Label, d.Target, d.Package))];

    private static IReadOnlyList<FakeDevice> Defaults() => [
        Build(Platforms.Ios, 0, null, null, FakeScenarios.Healthy, null, null, null, null),
        Build(Platforms.Android, 0, null, null, FakeScenarios.Healthy, null, null, null, null)
    ];

    private static List<FakeDevice> ReadDevices(IConfiguration section) {
        var perPlatform = new Dictionary<string, int>(StringComparer.Ordinal);
        var devices = new List<FakeDevice>();

        foreach (var child in section.GetChildren()) {
            if (!int.TryParse(child.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue;
            string platform = NormalizePlatform(child["Platform"]);
            int index = perPlatform.GetValueOrDefault(platform);
            perPlatform[platform] = index + 1;
            devices.Add(Build(platform, index, child["Label"], child["Package"], child["Scenario"],
                child["AppVersion"], child["Build"], child["ClientVersion"], child["ProbeDelayMs"]));
        }

        return devices;
    }

    private static FakeDevice Build(string platform, int index, string? label, string? package, string? scenario,
        string? appVersion, string? build, string? clientVersion, string? probeDelayMs) {
        string ordinal = index.ToString(CultureInfo.InvariantCulture);
        string parsedScenario = FakeScenarios.Parse(scenario);
        return new FakeDevice(
            $"{IdPrefix}{platform}-{ordinal}",
            platform,
            Label(label, platform, ordinal),
            $"{TargetPrefix}{parsedScenario}",
            string.IsNullOrWhiteSpace(package) ? DefaultPackage : package.Trim(),
            parsedScenario,
            NullIfEmpty(appVersion),
            NullIfEmpty(build),
            int.TryParse(clientVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cv) ? cv : null,
            int.TryParse(probeDelayMs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay) && delay >= 0
                ? delay
                : 400);
    }

    private static string Label(string? label, string platform, string ordinal) {
        string trimmed = label?.Trim() ?? "";
        if (trimmed.Length == 0) return $"{Marker} {platform} {ordinal}";
        return trimmed.Contains(Marker, StringComparison.OrdinalIgnoreCase) ? trimmed : $"{Marker} {trimmed}";
    }

    private static string NormalizePlatform(string? platform) =>
        Platforms.Matches(platform?.Trim(), Platforms.Ios) ? Platforms.Ios : Platforms.Android;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
