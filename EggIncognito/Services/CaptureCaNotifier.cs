using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Services;


public interface ICaptureCaNotifier {


    Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct);
}


public sealed record CaptureSetupDm(
    string DiscordId, byte[] CerBytes, string ProxyHost, int Port);
public sealed class NoopCaptureCaNotifier : ICaptureCaNotifier {
    public Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) => Task.FromResult(false);
}

public sealed class DiscordCaptureCaNotifier(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<DiscordCaptureCaNotifier> logger)
    : ICaptureCaNotifier {
    private const string ProfileFile = "eggincognito-capture.mobileconfig";

    public async Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) {
        var token = config["Discord:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(dm.DiscordId) || dm.CerBytes.Length == 0)
            return false;

        try {
            var http = httpFactory.CreateClient("discord-api");
            var channelId = await OpenDmAsync(http, token, dm.DiscordId, ct);
            if (channelId is null) return false;

            var profile = MobileConfig.BuildCaProfile(dm.CerBytes, dm.DiscordId);
            return await PostAsync(http, token, channelId, profile, ProfileFile, BuildMessage(dm), ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Capture setup DM to {DiscordId} failed; fail-closed", dm.DiscordId);
            return false;
        }
    }


    internal static string BuildMessage(CaptureSetupDm dm) =>
        $"""
        **Hosted capture is live.**

        **1. Install the CA** (attached). iOS: open it, then Settings > General > About > Certificate Trust Settings, turn it on. Android: needs root.
        **2. Set Wi-Fi proxy to Manual:**
        Server `{dm.ProxyHost}`
        Port `{dm.Port}`
        Auth off
        **3. Open Egg, Inc.**
        """;


    private static async Task<string?> OpenDmAsync(HttpClient http, string token, string discordId, CancellationToken ct) {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/users/@me/channels") {
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


    private static async Task<bool> PostAsync(
        HttpClient http, string token, string channelId, byte[] profile, string fileName, string content,
        CancellationToken ct) {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(profile);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-apple-aspen-config");
        form.Add(file, "files[0]", fileName);
        form.Add(new StringContent(
            JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json"), "payload_json");

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://discord.com/api/v10/channels/{channelId}/messages") {
            Content = form,
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }
}
