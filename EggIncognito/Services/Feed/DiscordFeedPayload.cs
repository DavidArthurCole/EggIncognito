using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public static class DiscordFeedPayload {
    public const string TestNotice = "EggIncognito feed test. Sample data, not a real release.";

    private const int MaxFieldChars = 900;
    private const int MaxListed = 12;

    public static string MarkAsTest(string body) {
        JsonNode? node;
        try {
            node = JsonNode.Parse(body);
        } catch (JsonException) {
            return body;
        }

        if (node is not JsonObject obj) return body;

        string existing = obj["content"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        obj["content"] = existing.Length > 0 ? $"{TestNotice}\n{existing}" : TestNotice;

        if (obj["embeds"] is JsonArray embeds) {
            foreach (var entry in embeds) {
                if (entry is JsonObject embed) embed["footer"] = new JsonObject { ["text"] = TestNotice };
            }
        }

        return obj.ToJsonString();
    }

    public static string Build(
        string platform, string appVersion, string build, string? clientVersion, string protoSha,
        bool protoChanged, string pageUrl, string? messageTemplate = null,
        VersionDelta delta = VersionDelta.Unknown, string? prevAppVersion = null, string? prevBuild = null,
        IReadOnlyList<string>? flaws = null) {
        if (!string.IsNullOrWhiteSpace(messageTemplate)) {
            var vars = FeedTemplate.BuildVars(platform, appVersion, build, clientVersion, protoSha, protoChanged,
                pageUrl, delta, prevAppVersion, prevBuild, flaws);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object> {
            new { name = "Proto", value = protoChanged ? "changed" : "unchanged", inline = true },
            new { name = "SHA", value = Short(protoSha), inline = true },
            new { name = "Delta", value = VersionDeltaCalc.Label(delta), inline = true }
        };
        if (!string.IsNullOrEmpty(clientVersion))
            fields.Add(new { name = "Client", value = clientVersion, inline = true });
        if (!string.IsNullOrEmpty(prevAppVersion))
            fields.Add(new { name = "Previous", value = $"{prevAppVersion} ({prevBuild})", inline = true });
        if (flaws is { Count: > 0 })
            fields.Add(new { name = "Flaws", value = string.Join(", ", flaws), inline = false });

        var embed = new {
            title = $"Egg, Inc. {appVersion} (build {build}, {platform})",
            url = pageUrl,
            color = flaws is { Count: > 0 } || delta is VersionDelta.Backfill or VersionDelta.Unknown
                ? 0xe05252
                : protoChanged
                    ? 0xef7559
                    : 0x5aa9e6,
            fields = fields.ToArray()
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }

    public static string BuildConfig(
        string feed, string feedLabel, string sha, string pageUrl, string? messageTemplate,
        IReadOnlyList<string> changed, IReadOnlyList<string> added, IReadOnlyList<string> removed) {
        if (!string.IsNullOrWhiteSpace(messageTemplate)) {
            var vars = FeedTemplate.ConfigVars(feed, feedLabel, sha, pageUrl, changed, added, removed);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object> {
            new { name = "Response", value = feedLabel, inline = true },
            new { name = "Hash", value = Short(sha), inline = true }
        };
        if (changed.Count > 0)
            fields.Add(new { name = "Changed", value = Listed(changed), inline = false });
        if (added.Count > 0)
            fields.Add(new { name = "Added", value = Listed(added), inline = false });
        if (removed.Count > 0)
            fields.Add(new { name = "Removed", value = Listed(removed), inline = false });

        var embed = new {
            title = $"Egg, Inc. {feedLabel} changed",
            url = pageUrl,
            color = changed.Count == 0 ? 0x5aa9e6 : 0x8b5cf6,
            fields = fields.ToArray()
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }

    public static string BuildGameData(
        string binaryVersion, string? prevBinaryVersion, string platform, string inputSha,
        IReadOnlyList<string> changedDocs, string pageUrl, string? messageTemplate) {
        if (!string.IsNullOrWhiteSpace(messageTemplate)) {
            var vars = FeedTemplate.GameDataVars(binaryVersion, prevBinaryVersion, platform, inputSha,
                changedDocs, pageUrl);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object> {
            new { name = "Binary", value = Or(binaryVersion, "unknown"), inline = true },
            new { name = "Platform", value = Or(platform, "unknown"), inline = true },
            new {
                name = "Documents",
                value = changedDocs.Count.ToString(CultureInfo.InvariantCulture),
                inline = true
            }
        };
        if (!string.IsNullOrEmpty(prevBinaryVersion) &&
            !string.Equals(prevBinaryVersion, binaryVersion, StringComparison.Ordinal)) {
            fields.Add(new { name = "Previous", value = prevBinaryVersion, inline = true });
        }
        if (changedDocs.Count > 0)
            fields.Add(new { name = "Changed", value = Listed(changedDocs), inline = false });

        var embed = new {
            title = $"Egg, Inc. game data rebuilt from {Or(binaryVersion, "an unknown binary")}",
            url = pageUrl,
            color = 0x3fa06a,
            fields = fields.ToArray()
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

    private static string Or(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    private static string Listed(IReadOnlyList<string> items) {
        var shown = items.Count <= MaxListed ? items : items.Take(MaxListed).ToList();
        string text = string.Join(", ", shown);
        if (text.Length > MaxFieldChars) text = text[..MaxFieldChars] + "...";
        int hidden = items.Count - shown.Count;
        return hidden > 0
            ? text + " (+" + hidden.ToString(CultureInfo.InvariantCulture) + " more)"
            : text;
    }
}
