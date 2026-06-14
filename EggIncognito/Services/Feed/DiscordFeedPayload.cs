using System.Text.Json;

namespace EggIncognito.Services.Feed;

public static class DiscordFeedPayload
{
    // The Discord webhook body for a proto event. One embed: title + fields + a link to the registry page.
    public static string Build(string platform, string version, string protoSha, bool protoChanged, string pageUrl)
    {
        var embed = new
        {
            title = $"Egg, Inc. {version} ({platform})",
            url = pageUrl,
            color = protoChanged ? 0xef7559 : 0x5aa9e6,
            fields = new object[]
            {
                new { name = "Proto", value = protoChanged ? "changed" : "unchanged", inline = true },
                new { name = "SHA", value = protoSha.Length > 12 ? protoSha[..12] : protoSha, inline = true },
            },
        };
        return JsonSerializer.Serialize(new { embeds = new[] { embed } });
    }
}
