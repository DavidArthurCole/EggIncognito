using System.Threading.Channels;
using EggIncognito.Core.Services;

namespace EggIncognito.Capture;

public sealed class CapturePipeline {
    public const string EidPlaceholder = "EI0000000000000000";

    public CapturePipeline(string contentRoot, string? eid, bool overwrite, bool writeEndpoints,
        IReadOnlyCollection<string>? liveRoutes, IEndpointWriteObserver? writeObserver, HarWriter? har,
        IRouteCatalog? catalog = null) {
        var routes = liveRoutes ?? [];
        if (writeEndpoints || routes.Count > 0) {
            Extractor = EndpointExtractor.ForRepo(contentRoot, eid, EidPlaceholder, overwrite);
            Extractor.Quiet = true;
            Extractor.LiveOnly = !writeEndpoints;
            Extractor.LiveRoutes = routes.ToHashSet(StringComparer.Ordinal);
            Extractor.WriteObserver = writeObserver;
        }

        Decoder = catalog is null ? new FlowDecoder(contentRoot) : new FlowDecoder(catalog);
        Processor = new FlowProcessor(Extractor, Decoder, har, contentRoot);
        Queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });
    }

    public EndpointExtractor? Extractor { get; }
    public FlowDecoder Decoder { get; }
    public FlowProcessor Processor { get; }
    public Channel<CapturedFlow> Queue { get; }

    public Task StartPump(CaptureHub hub, Func<string> now,
        Action<CapturedFlow>? onDequeued,
        Action<RinfoHarvester.ObservedVersion>? onObserved,
        Action<DashboardFlow>? onProcessed,
        CancellationToken ct,
        Func<DashboardFlow, DashboardFlow>? projectForHub = null) =>
        Task.Run(async () => {
            await foreach (var flow in Queue.Reader.ReadAllAsync()) {
                onDequeued?.Invoke(flow);
                try {
                    var dash = Processor.Process(flow);
                    if (dash.Observed is { } obs) onObserved?.Invoke(obs);
                    onProcessed?.Invoke(dash);
                    hub.Publish(projectForHub is null ? dash : projectForHub(dash), now());
                } catch (Exception ex) {
                    CaptureDiagnostics.Failed("pump", flow.Url, ex);
                }
            }
        }, ct);

    public void Attach(ICaptureProxy proxy, CaptureHub hub, Func<string> now,
        Action<CapturedFlow>? onFlowCaptured = null,
        Action<int, string?>? onClientConnected = null,
        Action<int, string?>? onClientDisconnected = null,
        Action? onAuxbrainConnect = null,
        Action<string>? onDecryptError = null,
        Action? onTrustRestored = null) {
        proxy.FlowCaptured += flow => {
            onFlowCaptured?.Invoke(flow);
            Queue.Writer.TryWrite(flow);
        };
        proxy.ClientConnected += (count, ip) => {
            onClientConnected?.Invoke(count, ip);
            hub.RecordConnection(count, ip, now());
        };
        proxy.ClientDisconnected += (count, ip) => {
            onClientDisconnected?.Invoke(count, ip);
            hub.RecordDisconnection(count, now());
        };
        proxy.AuxbrainConnect += () => {
            onAuxbrainConnect?.Invoke();
            hub.RecordAuxbrainConnect();
        };
        proxy.DecryptError += msg => {
            onDecryptError?.Invoke(msg);
            hub.RecordDecryptError(msg, now());
        };
        proxy.TrustRestored += () => {
            onTrustRestored?.Invoke();
            hub.RecordTrustRestored(now());
        };
    }
}
