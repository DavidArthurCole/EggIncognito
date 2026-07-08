using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Feed;

// Fans a registry event out to matching active Discord subscriptions. Best-effort per sub: a failure
// records a failed delivery + bumps fail_count; 404/410 deactivates the dead webhook. Idempotent via
// the unique (subscription, proto_version) delivery row.
public sealed class FeedDispatcher(
    IFeedSubscriptionStore store, IHttpClientFactory httpFactory, ILogger<FeedDispatcher> logger)
{
    private const int DeadAfterFailures = 5;

    // Default public host for the proto page links in feed payloads. Overridable via Feed:PageBaseUrl (no
    // trailing slash). Proto pages live at /protos/* on the main host.
    public const string DefaultPageBaseUrl = "https://eggincognito.davidarthurcole.me";

    public static string BuildPageUrl(string? baseUrl, string platform, string build) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? DefaultPageBaseUrl : baseUrl!.TrimEnd('/'))}/protos/{platform}/{build}";

    public async Task DispatchAsync(
        int protoVersionId, string platform, string appVersion, string build, string? clientVersion,
        string protoSha, bool created, bool protoChanged, string pageUrl, CancellationToken ct = default)
    {
        var subs = await store.ActiveAsync(ct);
        var http = httpFactory.CreateClient("discord-api");
        foreach (var sub in subs)
        {
            if (!FeedTrigger.Matches(sub.Trigger, created, protoChanged, sub.Platforms, platform)) continue;
            if (await store.AlreadyDeliveredAsync(sub.Id, protoVersionId, ct)) continue;

            int? code = null; var ok = false;
            try
            {
                var body = DiscordFeedPayload.Build(
                    platform, appVersion, build, clientVersion, protoSha, protoChanged, pageUrl, sub.MessageTemplate);
                var res = await http.PostAsync(sub.TargetUrl,
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
                code = (int)res.StatusCode;
                ok = res.IsSuccessStatusCode;
                if (code is 404 or 410) await store.SetActiveAsync(sub.Id, false, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "feed dispatch to sub {Id} threw", sub.Id); }

            await store.RecordAsync(new FeedDelivery
            {
                SubscriptionId = sub.Id, ProtoVersionId = protoVersionId,
                Status = ok ? "sent" : "failed", AttemptedAt = DateTimeOffset.UtcNow,
                ResponseCode = code, Attempts = 1,
            }, ct);

            if (ok) await store.MarkDeliveredAsync(sub.Id, DateTimeOffset.UtcNow, ct);
            else
            {
                await store.BumpFailAsync(sub.Id, ct);
                var refreshed = (await store.ActiveAsync(ct)).FirstOrDefault(s => s.Id == sub.Id);
                if (refreshed is not null && refreshed.FailCount >= DeadAfterFailures)
                    await store.SetActiveAsync(sub.Id, false, ct);
            }
        }
    }
}
