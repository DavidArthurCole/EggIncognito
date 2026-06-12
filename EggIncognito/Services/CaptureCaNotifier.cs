using System.Net.Http.Headers;
using System.Text.Json;

namespace EggIncognito.Services;

// Best-effort delivery of a freshly-minted capture CA to the user over a Discord DM. The web app never
// hard-depends on the bot: NoopCaptureCaNotifier is registered when Discord:BotToken is unset.
public interface ICaptureCaNotifier
{
    // Returns whether the DM (channel open + message with the .cer attachment) was delivered. Never
    // throws; any failure reports false so the caller can fall back to the /capture download button.
    Task<bool> SendCaAsync(string discordId, byte[] cerBytes, CancellationToken ct);
}

// No bot configured: nothing to send. The caller falls back to the download button.
public sealed class NoopCaptureCaNotifier : ICaptureCaNotifier
{
    public Task<bool> SendCaAsync(string discordId, byte[] cerBytes, CancellationToken ct) =>
        Task.FromResult(false);
}

// Sends the CA over Discord REST with the bot token, mirroring SupporterStatus's HttpClient pattern (no
// socket-client dependency). Opens a DM channel, then posts a multipart message carrying the .cer plus a
// short install hint. Fail-closed: any non-success or exception reports false.
public sealed class DiscordCaptureCaNotifier(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<DiscordCaptureCaNotifier> logger)
    : ICaptureCaNotifier
{
    private const string FileName = "eggincognito-capture-ca.cer";
    private const string Content =
        "Here is your EggIncognito capture CA. Install it on your device, then start a session at "
        + "/capture (https://eggincognito.davidarthurcole.me/capture).";

    public async Task<bool> SendCaAsync(string discordId, byte[] cerBytes, CancellationToken ct)
    {
        var token = config["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(discordId) || cerBytes.Length == 0)
            return false;

        try
        {
            var http = httpFactory.CreateClient("discord-api");

            var channelId = await OpenDmAsync(http, token, discordId, ct);
            if (channelId is null) return false;

            return await PostFileAsync(http, token, channelId, cerBytes, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CA DM to {DiscordId} failed; fail-closed", discordId);
            return false;
        }
    }

    // POST /users/@me/channels {"recipient_id": id} -> the DM channel id, or null on any failure.
    private static async Task<string?> OpenDmAsync(HttpClient http, string token, string discordId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/users/@me/channels")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { recipient_id = discordId }),
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    // POST /channels/{id}/messages as multipart/form-data: the .cer as files[0] plus a payload_json part.
    private static async Task<bool> PostFileAsync(
        HttpClient http, string token, string channelId, byte[] cerBytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(cerBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-x509-ca-cert");
        form.Add(file, "files[0]", FileName);
        form.Add(new StringContent(
            JsonSerializer.Serialize(new { content = Content }),
            System.Text.Encoding.UTF8, "application/json"), "payload_json");

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://discord.com/api/v10/channels/{channelId}/messages")
        {
            Content = form,
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }
}
