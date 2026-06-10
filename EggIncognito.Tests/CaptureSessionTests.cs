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
}
