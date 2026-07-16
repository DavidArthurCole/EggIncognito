using System.Text.Json;

namespace EggIncognito.Services.Feed;

public static class DiscordFeedPayload
{
   
   
   
    public static string Build(
        string platform, string appVersion, string build, string? clientVersion, string protoSha,
        bool protoChanged, string pageUrl, string? messageTemplate = null)
    {
        if (!string.IsNullOrWhiteSpace(messageTemplate))
        {
            var vars = FeedTemplate.BuildVars(platform, appVersion, build, clientVersion, protoSha, protoChanged, pageUrl);
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, vars) });
        }

        var fields = new List<object>
        {
            new { name = "Proto", value = protoChanged ? "changed" : "unchanged", inline = true },
            new { name = "SHA", value = protoSha.Length > 12 ? protoSha[..12] : protoSha, inline = true },
        };
        if (!string.IsNullOrEmpty(clientVersion))
            fields.Add(new { name = "Client", value = clientVersion, inline = true });

        var embed = new
        {
            title = $"Egg, Inc. {appVersion} (build {build}, {platform})",
            url = pageUrl,
            color = protoChanged ? 0xef7559 : 0x5aa9e6,
            fields = fields.ToArray(),
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }
}
