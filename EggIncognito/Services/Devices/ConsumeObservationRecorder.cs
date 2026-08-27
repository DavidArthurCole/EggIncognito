using System.Text.Json;
using System.Threading.Channels;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Observations;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Devices;

public sealed class ConsumeObservationRecorder(
    IServiceScopeFactory scopes,
    ILogger<ConsumeObservationRecorder> logger) : IProcessedFlowObserver, IHostedService, IDisposable {
    public const string ConsumeRoute = "ei_afx/consume_artifact";
    public const string DemoteRoute = "ei_afx/demote_artifact";

    private static readonly JsonSerializerOptions CamelJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Channel<ArtifactConsumeObservation> _queue =
        Channel.CreateUnbounded<ArtifactConsumeObservation>(new UnboundedChannelOptions { SingleReader = true });

    private CancellationTokenSource? _cts;
    private Task? _drain;

    public void OnFlowProcessed(string deviceId, DashboardFlow flow) {
        string? action = ActionFor(flow.Path);
        if (action is null) return;

        try {
            var row = Build(action, deviceId, flow);
            if (row is not null) _queue.Writer.TryWrite(row);
        } catch (Exception ex) {
            CaptureDiagnostics.Failed("consume-observation", flow.Url, ex);
        }
    }

    public static string? ActionFor(string path) =>
        string.Equals(path, ConsumeRoute, StringComparison.Ordinal) ? "consume"
        : string.Equals(path, DemoteRoute, StringComparison.Ordinal) ? "demote"
        : null;

    public static ArtifactConsumeObservation? Build(string action, string deviceId, DashboardFlow flow) {
        if (flow.RequestJsonRaw is null || flow.ResponseJsonRaw is null) return null;

        var request = JsonParser.Default.Parse<ConsumeArtifactRequest>(flow.RequestJsonRaw);
        var response = JsonParser.Default.Parse<ConsumeArtifactResponse>(flow.ResponseJsonRaw);
        if (request.Spec is null) return null;

        var byproducts = response.Byproducts
            .GroupBy(b => (b.Name, b.Level, b.Rarity))
            .Select(g => new ArtifactByproductRow(ProtoEnumNames.SpecName(g.Key.Name),
                ProtoEnumNames.LevelName(g.Key.Level), ProtoEnumNames.RarityName(g.Key.Rarity), g.Count()))
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ThenBy(b => b.Level, StringComparer.Ordinal)
            .ThenBy(b => b.Rarity, StringComparer.Ordinal)
            .ToArray();

        var rewards = response.OtherRewards
            .Select(r => new ArtifactRewardRow(ProtoEnumNames.RewardTypeName(r.RewardType),
                string.IsNullOrEmpty(r.RewardSubType) ? null : r.RewardSubType, r.RewardAmount))
            .ToArray();

        double goldenEggs = response.OtherRewards
            .Where(r => r.RewardType == RewardType.Gold)
            .Sum(r => r.RewardAmount);

        return new ArtifactConsumeObservation {
            Action = action,
            SpecName = ProtoEnumNames.SpecName(request.Spec.Name),
            SpecLevel = ProtoEnumNames.LevelName(request.Spec.Level),
            SpecRarity = ProtoEnumNames.RarityName(request.Spec.Rarity),
            CountRequested = (int)Math.Max(request.Quantity, 1),
            Byproducts = JsonSerializer.Serialize(byproducts, CamelJson),
            OtherRewards = JsonSerializer.Serialize(rewards, CamelJson),
            GoldenEggs = goldenEggs,
            Success = response.Success,
            ClientVersion = string.IsNullOrEmpty(request.Rinfo?.Version) ? null : request.Rinfo.Version,
            DeviceId = string.IsNullOrEmpty(deviceId) ? null : deviceId,
            ObservedAt = DateTimeOffset.UtcNow
        };
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _drain = Task.Run(() => DrainAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        _queue.Writer.TryComplete();
        if (_drain is null) return;

        try {
            await _drain.WaitAsync(cancellationToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "consume observation drain cancelled with rows still queued");
        } finally {
            if (_cts is not null) await _cts.CancelAsync();
        }
    }

    public void Dispose() => _cts?.Dispose();

    private async Task DrainAsync(CancellationToken ct) {
        try {
            await foreach (var row in _queue.Reader.ReadAllAsync(ct)) {
                try {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
                    db.ArtifactConsumeObservations.Add(row);
                    await db.SaveChangesAsync(ct);
                } catch (Exception ex) {
                    logger.LogWarning(ex, "consume observation write failed for {Action} {Spec}", row.Action,
                        row.SpecName);
                }
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "consume observation drain stopped");
        }
    }
}
