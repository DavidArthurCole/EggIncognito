using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Feed;


public sealed class FeedDispatcher(
    IFeedSubscriptionStore store, IHttpClientFactory httpFactory, ILogger<FeedDispatcher> logger)
{
    private const int DeadAfterFailures = 5;

   
   
    public const string DefaultPageBaseUrl = "https://eggincognito.davidarthurcole.me";

    public static string BuildPageUrl(string? baseUrl, string platform, string build) =>
        $"{(string.IsNullOrEmpty(baseUrl) ? DefaultPageBaseUrl : baseUrl!.TrimEnd('/'))}/protos/{platform}/{build}";

    public async Task DispatchAsync(INotificationEvent evt, CancellationToken ct = default)
    {
        var subs = await store.ActiveAsync(ct);
        var http = httpFactory.CreateClient("discord-api");
        foreach (var sub in subs)
        {
            if (!string.Equals(sub.EventKind, evt.EventKind, StringComparison.Ordinal)) continue;
            if (!evt.Matches(sub)) continue;
            if (await store.AlreadyDeliveredAsync(sub.Id, evt.EventKind, evt.DedupKey, ct)) continue;

            int? code = null; var ok = false;
            try
            {
                var body = evt.BuildBody(sub.MessageTemplate);
                var res = await http.PostAsync(sub.TargetUrl,
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
                code = (int)res.StatusCode;
                ok = res.IsSuccessStatusCode;
                if (code is 404 or 410) await store.SetActiveAsync(sub.Id, false, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "feed dispatch to sub {Id} threw", sub.Id); }

            await store.RecordAsync(new FeedDelivery
            {
                SubscriptionId = sub.Id, EventKind = evt.EventKind, DedupKey = evt.DedupKey,
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
