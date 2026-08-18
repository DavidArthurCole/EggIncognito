using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public sealed class ProtoUpsertNotifier(
    IServiceProvider services,
    IConfiguration config,
    ILogger<ProtoUpsertNotifier> logger) : IProtoUpsertObserver {
    public async Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        if (services.GetService<FeedDispatcher>() is not { } dispatcher) return;
        try {
            string pageUrl = FeedDispatcher.BuildPageUrl(
                config["Feed:PageBaseUrl"], notice.Platform, notice.Build);
            var flaws = ProtoVersionQuality.Flaws(
                notice.Platform, notice.Build, notice.ClientVersion, notice.ProtoSha, notice.HasProtoText);
            await dispatcher.DispatchAsync(new ProtoBuildEvent(
                notice.ProtoVersionId, notice.Platform, notice.AppVersion, notice.Build, notice.ClientVersion,
                notice.ProtoSha, notice.Created, notice.ProtoChanged, pageUrl,
                notice.Delta, notice.PrevAppVersion, notice.PrevBuild, flaws), ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "proto-build dispatch for {Platform} {Build} threw",
                notice.Platform, notice.Build);
        }
    }
}
