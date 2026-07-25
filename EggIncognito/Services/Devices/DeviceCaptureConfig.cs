namespace EggIncognito.Services.Devices;

public sealed record DeviceCaptureConfig {
    public bool Enabled { get; init; }
    public int BasePort { get; init; } = 9100;
    public string? HostIp { get; init; }
    public bool Verbose { get; init; }

    public string? IosSshHost { get; init; }
    public string IosSshPort { get; init; } = "2222";
    public string? IosSshKeyPath { get; init; }
    public string? IosSetCommand { get; init; }
    public string? IosClearCommand { get; init; }
    public string? IosNetworkServiceGuid { get; init; }
    public string? IosPlutilPath { get; init; }
    public string? IosPreferencesPlist { get; init; }

    public string? AndroidCaInstallScript { get; init; }
    public string? IosCaInstallCommand { get; init; }
    public string? IosTrustStorePath { get; init; }
    public string? IosAppProcessName { get; init; }
    public string? IosRestartCommand { get; init; }

    public static DeviceCaptureConfig Bind(IConfiguration config) {
        var dc = config.GetSection("DeviceCapture");
        var ios = dc.GetSection("Ios");
        var android = dc.GetSection("Android");
        var upd = config.GetSection("DeviceUpdate").GetSection("Ios");

        return new DeviceCaptureConfig {
            Enabled = dc.GetValue("Enabled", false),
            BasePort = dc.GetValue("BasePort", 9100),
            Verbose = dc.GetValue("Verbose", false),
            HostIp = Nz(dc["HostIp"]),
            IosSshHost = Nz(ios["SshHost"]) ?? Nz(upd["SshHost"]),
            IosSshPort = Nz(ios["SshPort"]) ?? Nz(upd["SshPort"]) ?? "2222",
            IosSshKeyPath = Nz(ios["SshKeyPath"]) ?? Nz(upd["SshKeyPath"]),
            IosSetCommand = Nz(ios["SetCommand"]),
            IosClearCommand = Nz(ios["ClearCommand"]),
            IosNetworkServiceGuid = Nz(ios["NetworkServiceGuid"]),
            IosPlutilPath = Nz(ios["PlutilPath"]),
            IosPreferencesPlist = Nz(ios["PreferencesPlist"]),
            AndroidCaInstallScript = Nz(android["CaInstallScript"]),
            IosCaInstallCommand = Nz(ios["CaInstallCommand"]),
            IosTrustStorePath = Nz(ios["TrustStorePath"]),
            IosAppProcessName = Nz(ios["AppProcessName"]),
            IosRestartCommand = Nz(ios["RestartCommand"])
        };
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
