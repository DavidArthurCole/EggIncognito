using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests;

public class DeviceCaptureVerdictTests
{
    private static DeviceCaptureVerdict.Verdict V(bool listening, int port, long client, long aux, long flows, long rinfo, string? err = null)
        => DeviceCaptureVerdict.For(new DeviceCaptureVerdict.Counters(listening, port, client, aux, flows, rinfo, err));

    [Fact]
    public void NotListening_IsListenerDown() =>
        Assert.Equal("listener down", V(false, 0, 0, 0, 0, 0).Label);

    [Fact]
    public void RinfoHarvested_IsOk() =>
        Assert.Equal("ok", V(true, 100, 5, 5, 5, 2).Label);

    [Fact]
    public void FlowsButNoRinfo_IsNoRinfo() =>
        Assert.Equal("no rinfo in flows", V(true, 100, 5, 5, 3, 0).Label);

    [Fact]
    public void AuxbrainButNoFlows_IsCaUntrusted() =>
        Assert.Equal("CA untrusted", V(true, 100, 5, 2, 0, 0).Label);

    [Fact]
    public void ClientButNoAuxbrain_IsNotReachingAuxbrain() =>
        Assert.Equal("not reaching auxbrain", V(true, 100, 5, 0, 0, 0).Label);

    [Fact]
    public void ListeningButNoClient_IsNotRouting() =>
        Assert.Equal("not routing", V(true, 100, 0, 0, 0, 0).Label);

    [Fact]
    public void CaUntrusted_SurfacesDecryptError() =>
        Assert.Contains("boom", V(true, 100, 5, 2, 0, 0, "boom").Detail);
}
