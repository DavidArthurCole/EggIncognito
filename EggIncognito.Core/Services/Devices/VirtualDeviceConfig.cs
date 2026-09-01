using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Core.Services.Devices;

public sealed record VirtualDeviceConfig {
    public const string DefaultImage = "redroid/redroid:12.0.0_64only-latest";
    public const string DefaultSocket = "/var/run/docker.sock";

    public const string DefaultGmsPackage = "com.google.android.gms";

    public bool Enabled { get; init; }
    public string Kind { get; init; } = "redroid";
    public string Image { get; init; } = DefaultImage;
    public int MaxInstances { get; init; } = 4;
    public string DockerSocket { get; init; } = DefaultSocket;
    public int ReconcileSeconds { get; init; } = 20;
    public bool RequireGooglePlay { get; init; } = true;
    public string GmsPackage { get; init; } = DefaultGmsPackage;

    public static VirtualDeviceConfig Bind(IConfiguration config) {
        var v = config.GetSection("Devices").GetSection("Virtual");
        return new VirtualDeviceConfig {
            Enabled = Flag(v, "Enabled"),
            Kind = Nz(v["Kind"]) ?? "redroid",
            Image = Nz(v["Image"]) ?? DefaultImage,
            MaxInstances = Num(v, "MaxInstances", 4),
            DockerSocket = Nz(v["DockerSocket"]) ?? DefaultSocket,
            ReconcileSeconds = Num(v, "ReconcileSeconds", 20),
            RequireGooglePlay = Flag(v, "RequireGooglePlay", true),
            GmsPackage = Nz(v["GmsPackage"]) ?? DefaultGmsPackage
        };
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool Flag(IConfiguration config, string key, bool fallback = false) {
        string? raw = Nz(config[key]);
        if (raw is null) return fallback;
        return bool.TryParse(raw, out bool parsed) ? parsed : raw == "1";
    }

    private static int Num(IConfiguration config, string key, int fallback) =>
        int.TryParse(Nz(config[key]), CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
}
