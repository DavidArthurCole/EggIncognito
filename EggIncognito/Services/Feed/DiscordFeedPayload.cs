using System.Text.Json;

namespace EggIncognito.Services.Feed;

public static class DiscordFeedPayload {
    public static string Build(
        string platform, string appVersion, string build, string? clientVersion, string protoSha,
        bool protoChanged, string pageUrl, string? messageTemplate = null) {
        if (!string.IsNullOrWhiteSpace(messageTemplate)) {
            var vars = FeedTemplate.BuildVars(platform, appVersion, build, clientVersion, protoSha, protoChanged,
                pageUrl);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object> {
            new { name = "Proto", value = protoChanged ? "changed" : "unchanged", inline = true },
            new { name = "SHA", value = protoSha.Length > 12 ? protoSha[..12] : protoSha, inline = true }
        };
        if (!string.IsNullOrEmpty(clientVersion))
            fields.Add(new { name = "Client", value = clientVersion, inline = true });

        var embed = new {
            title = $"Egg, Inc. {appVersion} (build {build}, {platform})",
            url = pageUrl,
            color = protoChanged ? 0xef7559 : 0x5aa9e6,
            fields = fields.ToArray()
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }

    public static string BuildPeriodicals(string feed, string sha, string pageUrl, string? messageTemplate = null,
        PeriodicalsAspectSummary? aspects = null) {
        if (!string.IsNullOrWhiteSpace(messageTemplate)) {
            var vars = FeedTemplate.PeriodicalsVars(feed, sha, pageUrl, aspects);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object> {
            new { name = "Feed", value = feed, inline = true },
            new { name = "Hash", value = sha.Length > 12 ? sha[..12] : sha, inline = true }
        };
        if (aspects is not null) {
            if (aspects.ChangedAspects.Count > 0)
                fields.Add(new { name = "Changed", value = string.Join(", ", aspects.ChangedAspects), inline = true });
            if (aspects.AddedEvents.Count > 0)
                fields.Add(new { name = "New events", value = string.Join(", ", aspects.AddedEvents), inline = false });
            if (aspects.AddedContracts.Count > 0) {
                fields.Add(new {
                    name = "New contracts",
                    value = string.Join(", ", aspects.AddedContracts),
                    inline = false
                });
            }

            if (aspects.AddedColleggtibles.Count > 0) {
                fields.Add(new {
                    name = "New colleggtibles",
                    value = string.Join(", ", aspects.AddedColleggtibles),
                    inline = false
                });
            }
        }

        var embed = new {
            title = $"Egg, Inc. periodicals changed: {feed}",
            url = pageUrl,
            color = 0x8b5cf6,
            fields = fields.ToArray()
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }
}
