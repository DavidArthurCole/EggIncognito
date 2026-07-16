using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Backfill;
public sealed record VersionPollerOptions
{
    public bool Enabled { get; init; } = true;
    public int PollIntervalMinutes { get; init; } = 360;
    public string[] Platforms { get; init; } = ["android", "ios"];
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
