using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class FakeDeviceSettingsProvider : ISettingsProvider {
    private const string Category = "Devices: fake";

    private const string GateNote =
        "Fake devices only load in a Staging instance running AppMode=Local. "
        + "Startup throws if this is set in Production or with AppMode=Hosted.";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("devices.fake.enabled", "Devices__Fake__Enabled", "Fake devices enabled", Category,
            SettingKind.Bool, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Default = "false", Description = GateNote
        },
        new("devices.fake.sweep_minutes", "Devices__Fake__SweepMinutes", "Sweep interval (minutes)", Category,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "15", Description = GateNote },
        new("devices.fake.slow_entry_ms", "Devices__Fake__SlowEntryMs", "Slow entry (ms)", Category,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "45000", Description = GateNote },
        new("devices.fake.wedge_backdate_minutes", "Devices__Fake__WedgeBackdateMinutes",
            "Wedge backdate (minutes)", Category,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "35", Description = GateNote },
        new("devices.fake.capture_interval_ms", "Devices__Fake__CaptureIntervalMs", "Capture interval (ms)", Category,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "4000", Description = GateNote }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
