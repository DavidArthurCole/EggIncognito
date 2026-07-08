using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class CaptureSessionTests
{
    private static CaptureSession NewSession(out FakeCaptureProxy fake)
    {
        var f = new FakeCaptureProxy();
        fake = f;
        var contentRoot = RealContentRoot();
        var tmp = Path.Combine(Path.GetTempPath(), "egi-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var opts = new CaptureSessionOptions(Port: 18080, Eid: null, Label: null,
            Overwrite: false, Verbose: false, CapturePath: tmp, CaPath: Path.Combine(tmp, "ca.cer"));
        return new CaptureSession(contentRoot, opts, _ => f);
    }

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

    [Fact]
    public async Task Start_ProxyStartThrows_ResetsToStopped_AndRemainsRestartable()
    {
        var s = NewSession(out var fake);
        fake.ThrowOnStart = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StartAsync(CancellationToken.None));
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(s.Hub.DevicesChanged);
        Assert.Equal(1, fake.DisposeCount);

        fake.ThrowOnStart = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        Assert.Equal(2, fake.StartCount);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_ProxyStopThrows_StillResetsToStopped()
    {
        var s = NewSession(out var fake);
        await s.StartAsync(CancellationToken.None);
        fake.ThrowOnStop = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.StopAsync());
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.False(s.Status.Running);
        Assert.Equal(1, fake.DisposeCount);

        fake.ThrowOnStop = false;
        await s.StartAsync(CancellationToken.None);
        Assert.True(s.Status.Running);
        await s.StopAsync();
    }

    [Fact]
    public async Task Stop_ClearsDevicesChangedSubscription()
    {
        var s = NewSession(out _);
        await s.StartAsync(CancellationToken.None);
        Assert.NotNull(s.Hub.DevicesChanged);
        await s.StopAsync();
        Assert.Null(s.Hub.DevicesChanged);
    }

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
