using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class CaptureSettingsProvider : ISettingsProvider {
    private const string Hosted = "Capture: hosted";
    private const string Local = "Capture: local";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("capture.hosted_enabled", "HostedCaptureEnabled", "Hosted capture enabled", Hosted,
            SettingKind.Bool, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Default = "false",
            Description = "Also read by the container entrypoint before the app starts, to add the any-IP route."
        },
        new("capture.ipv6_prefix", "Capture__Ipv6Prefix", "IPv6 prefix", Hosted,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Consumed by the entrypoint before boot. Changing it here alone does nothing."
        },
        new("capture.address_secret", "Capture__AddressSecret", "Address HMAC secret", Hosted,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret) {
            Description = "Startup fails when hosted capture is on and this is empty."
        },
        new("capture.front_door_port", "Capture__FrontDoorPort", "Front door port", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "8443" },
        new("capture.port_pool_base", "Capture__PortPoolBase", "Port pool base", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "24000" },
        new("capture.max_concurrent_sessions", "Capture__MaxConcurrentSessions", "Max concurrent sessions", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "10" },
        new("capture.max_idle_minutes", "Capture__MaxIdleMinutes", "Max idle (minutes)", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "30" },
        new("capture.max_session_hours", "Capture__MaxSessionHours", "Max session (hours)", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "4" },
        new("capture.max_limited_sessions", "Capture__MaxLimitedSessions", "Max limited sessions", Hosted,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "50" },
        new("capture.public_host", "Capture__PublicHost", "Public host", Hosted,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("capture.extra_allowed_hosts", "Capture__ExtraAllowedHosts", "Extra allowed hosts", Hosted,
            SettingKind.StringList, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Comma separated. The stack may also set indexed Capture__ExtraAllowedHosts__0 entries."
        },

        new("capture.path", "CapturePath", "Capture directory", Local,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("capture.ca_path", "CaPath", "CA certificate path", Local,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("capture.port", "CapturePort", "Local capture port", Local,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "8080" },
        new("capture.label", "CaptureLabel", "Capture label", Local,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("capture.overwrite", "CaptureOverwrite", "Overwrite captures", Local,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("capture.verbose", "CaptureVerbose", "Verbose capture", Local,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "false" },
        new("capture.egg_inc_eid", "EGG_INC_EID", "Egg Inc device id", Local,
            SettingKind.Text, ApplyTier.RestartRequired, Sensitivity.Plain)
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
