using EggIncognito.Capture;

namespace EggIncognito.Tests;

// Pure/seam-level guards on UnobtaniumCaptureProxy. Constructing the proxy is side-effect free;
// nothing here starts a listener or touches the cert store.
public class UnobtaniumCaptureProxyTests
{
    // The per-CONNECT decrypt decision. The CONNECT target is an authority that usually carries
    // ":443"; before normalization that port made auxbrain traffic tunnel undecrypted.
    [Theory]
    [InlineData("www.auxbrain.com", true)]
    [InlineData("www.auxbrain.com:443", true)]
    [InlineData("auxbrainhome.appspot.com:443", true)]
    [InlineData("ctx-dot-auxbrainhome.appspot.com:443", true)]
    [InlineData("example.com", false)]
    [InlineData("example.com:443", false)]
    [InlineData("auxbrainhome.appspot.com.evil.com:443", false)]
    public void ShouldDecrypt_DecidesByNormalizedHost(string connectAuthority, bool expected) =>
        Assert.Equal(expected, UnobtaniumCaptureProxy.ShouldDecrypt(connectAuthority));

    // A request whose response never arrives must not leak its stash for the session.
    [Fact]
    public void SweepStalePending_DropsExpired_KeepsFresh()
    {
        var proxy = new UnobtaniumCaptureProxy();
        var now = DateTime.UtcNow;
        var sweepAt = now + TimeSpan.FromMinutes(3); // past the once-per-TTL throttle
        proxy.StashPendingForTest("stale", now - TimeSpan.FromMinutes(5));
        proxy.StashPendingForTest("fresh", sweepAt - TimeSpan.FromSeconds(30));
        Assert.Equal(2, proxy.PendingRequestCount);

        proxy.SweepStalePending(sweepAt);

        Assert.Equal(1, proxy.PendingRequestCount);
    }

    [Fact]
    public void SweepStalePending_ThrottledWithinTtlWindow()
    {
        var proxy = new UnobtaniumCaptureProxy();
        var now = DateTime.UtcNow;
        proxy.StashPendingForTest("stale", now - TimeSpan.FromMinutes(10));

        // Within one TTL of construction the sweep is a no-op, so per-request overhead stays nil.
        proxy.SweepStalePending(now + TimeSpan.FromSeconds(30));

        Assert.Equal(1, proxy.PendingRequestCount);
    }

    // Handler failures used to vanish into the verbose-only trace; they must hit the DecryptError
    // channel and the counter.
    [Fact]
    public void ReportFlowError_RaisesDecryptError_AndCounts()
    {
        var proxy = new UnobtaniumCaptureProxy();
        string? reported = null;
        proxy.DecryptError += msg => reported = msg;

        proxy.ReportFlowError("request", new InvalidOperationException("boom"));

        Assert.Equal(1, proxy.FlowErrorCount);
        Assert.NotNull(reported);
        Assert.Contains("request", reported);
        Assert.Contains("boom", reported);
    }
}
