using EggIncognito.Capture;

namespace EggIncognito.Tests;

// CaptureSession owns proxy start/stop lifecycle. These guard idempotency + restartability +
// status, using FakeCaptureProxy so no real listener/CA is touched.
public class CaptureSessionTests
{
    private static CaptureSession NewSession(out FakeCaptureProxy fake)
    {
        var f = new FakeCaptureProxy();
        fake = f;
        // contentRoot must be the real EggIncognito project dir (EndpointExtractor.ForRepo reads
        // routes.yaml under it). All WRITES are redirected to a temp dir (CapturePath/CaPath); with
        // no flows processed, the extractor stays clean and Save() is a no-op, so the test never
        // mutates the repo.
        var contentRoot = RealContentRoot();
        var tmp = Path.Combine(Path.GetTempPath(), "egi-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var opts = new CaptureSessionOptions(Port: 18080, Eid: null, Label: null,
            Overwrite: false, Verbose: false, CapturePath: tmp, CaPath: Path.Combine(tmp, "ca.cer"));
        return new CaptureSession(contentRoot, opts, _ => f);
    }

    // Walk up to the source-tree EggIncognito project dir (the one holding RouteMap/routes.yaml).
    private static string RealContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "EggIncognito", "RouteMap", "routes.yaml");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "EggIncognito");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the EggIncognito project content root.");
    }

    [Fact]
    public async Task Start_SetsRunning_AndStartsProxyOnce()
    {
        var s = NewSession(out var fake);
        var result = await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(1, fake.StartCount);
        Assert.Equal(18080, result.Port);
        await s.StopAsync();
    }

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StartAsync(CancellationToken.None);
        Assert.Equal(1, fake.StartCount);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_IsIdempotent_AndStopsProxy()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StopAsync();
        await s.StopAsync();
        Assert.Equal(1, fake.StopCount);
        Assert.False(s.Status.Running);
    }

    [Fact]
    public async Task StartStopStart_RoundTrips()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        await s.StopAsync();
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(2, fake.StartCount);
        await s.StopAsync();
    }

    // P0.1 regression: a proxy start failure must tear down partial state and land on Stopped,
    // not wedge at Starting with a leaked proxy/queue/consumer/DeviceStore subscription.
    [Fact]
    public async Task Start_ProxyStartThrows_ResetsToStopped_AndRemainsRestartable()
    {
        var s = NewSession(out var fake);
        fake.ThrowOnStart = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StartAsync(CancellationToken.None));
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(s.Hub.DevicesChanged);
        Assert.Equal(1, fake.DisposeCount); // partial proxy was disposed, not leaked

        fake.ThrowOnStart = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(2, fake.StartCount);
        await s.StopAsync();
    }

    // P0.2 regression: a failure while stopping must not wedge State at Stopping; the finally
    // path always lands back on Stopped and the session can start again.
    [Fact]
    public async Task Stop_ProxyStopThrows_StillResetsToStopped()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        fake.ThrowOnStop = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StopAsync());
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.False(s.Status.Running);
        Assert.Equal(1, fake.DisposeCount); // proxy still disposed despite the stop failure

        fake.ThrowOnStop = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        await s.StopAsync();
    }

    // Stop must drop the per-run DeviceStore subscription, not leave it dangling on the
    // session-lifetime Hub until the next start happens to overwrite it.
    [Fact]
    public async Task Stop_ClearsDevicesChangedSubscription()
    {
        var s = NewSession(out _);
        await s.StartAsync(CancellationToken.None);
        Assert.NotNull(s.Hub.DevicesChanged);
        await s.StopAsync();
        Assert.Null(s.Hub.DevicesChanged);
    }

    // _activeClients is written from proxy event threads and read by Status; guard the
    // functional path: connect events show up in Status, stop resets to zero.
    [Fact]
    public async Task Status_ReflectsActiveClients_AndResetsOnStop()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        fake.EmitConnect(3, "192.168.1.50");
        Assert.Equal(3, s.Status.ActiveClients);
        await s.StopAsync();
        Assert.Equal(0, s.Status.ActiveClients);
    }
}
