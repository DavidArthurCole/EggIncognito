using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Services;

// Best-effort delivery of a freshly-minted capture CA + proxy details to the user over a Discord DM.
// The web app never hard-depends on the bot: NoopCaptureCaNotifier is registered when Discord:BotToken
// is unset.
public interface ICaptureCaNotifier
{
    // Returns whether the DM (channel open + message with the CA profile + token/proxy text) was
    // delivered. Never throws; any failure reports false so the caller falls back to the /capture card.
    Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct);
}

// Everything the DM needs: the public CA bytes (DER) plus the per-user proxy address to print as
// copyable text. The front door identifies the user by destination address, so the proxy needs no
// username or password.
public sealed record CaptureSetupDm(
    string DiscordId, byte[] CerBytes, string ProxyHost, int Port);

// No bot configured: nothing to send. The caller falls back to the download button.
public sealed class NoopCaptureCaNotifier : ICaptureCaNotifier
{
    public Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) => Task.FromResult(false);
}

// Sends the setup over Discord REST with the bot token, mirroring SupporterStatus's HttpClient pattern
// (no socket-client dependency). Opens a DM channel, then posts a multipart message carrying a
// .mobileconfig (one-tap CA install on iOS) plus a text block with the per-user proxy host/port.
// Fail-closed: any non-success or exception reports false.
public sealed class DiscordCaptureCaNotifier(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<DiscordCaptureCaNotifier> logger)
    : ICaptureCaNotifier
{
    private const string ProfileFile = "eggincognito-capture.mobileconfig";

    public async Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct)
    {
        var token = config["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(dm.DiscordId) || dm.CerBytes.Length == 0)
            return false;

        try
        {
            var http = httpFactory.CreateClient("discord-api");
            var channelId = await OpenDmAsync(http, token, dm.DiscordId, ct);
            if (channelId is null) return false;

            var profile = MobileConfig.BuildCaProfile(dm.CerBytes, dm.DiscordId);
            return await PostAsync(http, token, channelId, profile, ProfileFile, BuildMessage(dm), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Capture setup DM to {DiscordId} failed; fail-closed", dm.DiscordId);
            return false;
        }
    }

    // The copyable connection block. iOS: open the attached profile, install, trust. The front door
    // identifies the user by the per-user proxy address, so no username or password is needed.
    internal static string BuildMessage(CaptureSetupDm dm) =>
        $"""
        **EggIncognito hosted capture setup**

        Open the attached profile on iOS to install the CA in one tap (then enable full trust under
        Settings > General > About > Certificate Trust Settings). Android needs a rooted device with
        the CA in the system trust store.

        Then set your device Wi-Fi proxy to:
        Proxy: `{dm.ProxyHost}:{dm.Port}`  (no username or password, authentication off)
        Tap the value above to copy it. Your session is live: install the profile, set the proxy,
        then open Egg, Inc.
        """;

    // POST /users/@me/channels {"recipient_id": id} -> the DM channel id, or null on any failure.
    private static async Task<string?> OpenDmAsync(HttpClient http, string token, string discordId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/users/@me/channels")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { recipient_id = discordId }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    // POST /channels/{id}/messages as multipart: the .mobileconfig as files[0] + a payload_json part.
    private static async Task<bool> PostAsync(
        HttpClient http, string token, string channelId, byte[] profile, string fileName, string content,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(profile);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-aspen-config");
        form.Add(file, "files[0]", fileName);
        form.Add(new StringContent(
            JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json"), "payload_json");

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
