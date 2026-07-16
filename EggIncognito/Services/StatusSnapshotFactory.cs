using EggIncognito.Bot;
using EggIncognito.Capture;
using EggIncognito.Services;

namespace EggIncognito.Services;


public sealed record RepoUrl(string Value);


public sealed class StatusSnapshotFactory(
    IAppMode mode,
    CaptureSession capture,
    ITransportPipeline pipeline,
    IConfiguration config,
    RepoUrl repoUrl) : IStatusProvider
{
   
   
    private static readonly DateTimeOffset ProcessStart =
        new(System.Diagnostics.Process.GetCurrentProcess().StartTime);
    private readonly bool _dbEnabled = !string.IsNullOrWhiteSpace(config.GetConnectionString("Postgres"));

    public StatusSnapshot Build()
    {
        var stats = capture.Hub.StatsSnapshot();
        var (ok, empty, missing) = ClassifyEndpoints();
        return new StatusSnapshot(
            Mode: mode.Mode.ToString(),
            CanCapture: mode.CanCapture,
            CanWrite: mode.CanWrite,
            CaptureState: capture.State.ToString(),
            CaptureRunning: capture.State == CaptureState.Running,
            FlowsCaptured: stats.CapturedAuxbrain,
            DeviceCount: stats.DeviceCount,
            BytesCaptured: stats.BytesCaptured,
            DbEnabled: _dbEnabled,
            SigningReady: pipeline.CanSign,
            Uptime: DateTimeOffset.UtcNow - ProcessStart,
            Build: BuildInfo.FromAssembly(repoUrl.Value),
            EndpointsOk: ok, EndpointsEmpty: empty, EndpointsMissing: missing);
    }

    private (int Ok, int Empty, int Missing) ClassifyEndpoints()
    {
        try
        {
            var root = ContentRoot.Resolve(config["ContentRoot"]);
            var yaml = Path.Combine(root, "RouteMap", "routes.yaml");
            var defaults = Path.Combine(root, "Endpoints", "default");
            var r = EndpointStatus.Classify(yaml, defaults);
            return (r.Ok.Count, r.Empty.Count, r.Missing.Count);
        }
        catch { return (0, 0, 0); }
    }
}
