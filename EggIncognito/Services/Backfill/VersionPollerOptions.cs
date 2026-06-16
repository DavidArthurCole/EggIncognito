using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Backfill;

// Tunables for the store-version poller, bound from the "VersionPoller" config section. Default-on
// (when a DB is configured); set Enabled=false to turn the background poll off entirely.
public sealed record VersionPollerOptions
{
    public bool Enabled { get; init; } = true;
    public int PollIntervalMinutes { get; init; } = 360;
    public string[] Platforms { get; init; } = ["android", "ios"];
    // When true, a newly discovered version also queues an extract job (Android runs end-to-end via the
    // APK toolchain; iOS records intent until a binary is supplied).
    public bool AutoQueueExtract { get; init; } = true;

    public static VersionPollerOptions Bind(IConfiguration config)
    {
        var s = config.GetSection("VersionPoller");
        return new VersionPollerOptions
        {
            Enabled = s.GetValue("Enabled", true),
            PollIntervalMinutes = s.GetValue("PollIntervalMinutes", 360),
            Platforms = s.GetSection("Platforms").Get<string[]>() ?? ["android", "ios"],
            AutoQueueExtract = s.GetValue("AutoQueueExtract", true),
        };
    }
}
