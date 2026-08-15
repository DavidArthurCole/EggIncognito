using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices.Fake;

public static class FakeScenarios {
    public const string Healthy = "healthy";
    public const string StoreAhead = "store-ahead";
    public const string SlowHarvest = "slow-harvest";
    public const string FailingEntry = "failing-entry";
    public const string Unreachable = "unreachable";
    public const string Wedged = "wedged";

    public static readonly IReadOnlyList<string> All =
        [Healthy, StoreAhead, SlowHarvest, FailingEntry, Unreachable, Wedged];

    public static string Parse(string? value) {
        string trimmed = value?.Trim() ?? "";
        return All.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)) ?? Healthy;
    }
}

public static class FakeFixtureTiers {
    public const string Clone = "clone";
    public const string Synthesized = "synthesized";
}

public sealed record FakeDevice(
    string Id,
    string Platform,
    string Label,
    string Target,
    string Package,
    string Scenario,
    string? AppVersion,
    string? Build,
    int? ClientVersion,
    int ProbeDelayMs);

public sealed record FakeDeviceSettings(
    IReadOnlyList<FakeDevice> Devices,
    int SweepMinutes,
    int SlowEntryMs,
    int WedgeBackdateMinutes,
    int CaptureIntervalMs) {
    public static FakeDeviceSettings Empty { get; } = new([], 15, 45000, 35, 4000);

    public FakeDevice? For(string deviceId) =>
        Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
}

public sealed class FakeDeviceVersions {
    private readonly ConcurrentDictionary<string, (string? Version, string? Build)> _installed =
        new(StringComparer.OrdinalIgnoreCase);

    public (string? Version, string? Build) Get(string deviceId) =>
        _installed.TryGetValue(deviceId, out var v) ? v : (null, null);

    public void Set(string deviceId, string? version, string? build) =>
        _installed[deviceId] = (version, build);
}
