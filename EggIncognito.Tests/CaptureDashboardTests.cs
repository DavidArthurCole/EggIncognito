using Google.Protobuf;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class CaptureDashboardTests
{
    private static DashboardFlow F(string path = "ei/x") =>
        new(0, "", path, "POST", 200, null, null, "AAEC", null);

    [Fact]
    public void Publish_AssignsMonotonicIds_StartingAtOne()
    {
        var hub = new CaptureHub();
        var first = hub.Publish(F(), "t1");
        var second = hub.Publish(F(), "t2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first!.Id);
        Assert.Equal(2, second!.Id);
    }

    [Fact]
    public void Publish_StampsProvidedTimestamp()
    {
        var hub = new CaptureHub();
        var stored = hub.Publish(F(), "2026-06-06T00:00:00Z");

        Assert.NotNull(stored);
        Assert.Equal("2026-06-06T00:00:00Z", stored!.Timestamp);
    }

    [Fact]
    public void Snapshot_ReturnsPublishedFlows_OldestFirst()
    {
        var hub = new CaptureHub();
        hub.Publish(F("ei/a"), "t1");
        hub.Publish(F("ei/b"), "t2");
        hub.Publish(F("ei/c"), "t3");

        var snap = hub.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("ei/a", snap[0].Path);
        Assert.Equal("ei/b", snap[1].Path);
        Assert.Equal("ei/c", snap[2].Path);
    }

    [Fact]
    public void MarkSaved_FlipsBufferedFlow_SoRefreshDoesNotRePrompt()
    {
        var hub = new CaptureHub();
        var stored = hub.Publish(F("ei/bot_first_contact"), "t1");
        Assert.NotNull(stored);
        Assert.False(stored!.Saved);

        hub.MarkSaved(stored.Id);

        var replayed = Assert.Single(hub.Snapshot(), f => f.Id == stored.Id);
        Assert.True(replayed.Saved);
    }

    [Fact]
    public void Publish_WhenPaused_ReturnsNull_AndDoesNotBuffer()
    {
        var hub = new CaptureHub();
        hub.Publish(F(), "t1");
        var before = hub.Snapshot().Count;

        hub.Paused = true;
        var result = hub.Publish(F(), "t2");

        Assert.Null(result);
        Assert.Equal(before, hub.Snapshot().Count);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var hub = new CaptureHub();
        hub.Publish(F(), "t1");
        hub.Publish(F(), "t2");

        hub.Clear();

        Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public void Find_ReturnsMatchingFlow_NullForMissing()
    {
        var hub = new CaptureHub();
        var stored = hub.Publish(F("ei/found"), "t1");

        Assert.NotNull(stored);
        var found = hub.Find(stored!.Id);
        Assert.NotNull(found);
        Assert.Equal("ei/found", found!.Path);
        Assert.Equal(stored.Id, found.Id);

        Assert.Null(hub.Find(9999));
    }

    [Fact]
    public void Subscribe_DeliversPublishedFlow_ThenStopsAfterDispose()
    {
        var hub = new CaptureHub();
        var (reader, subscription) = hub.Subscribe();

        hub.Publish(F("ei/live"), "t1");
        var flowsBefore = DrainFlowPaths(reader);
        Assert.Contains("ei/live", flowsBefore);

        subscription.Dispose();
        hub.Publish(F("ei/after"), "t2");
        var flowsAfter = DrainFlowPaths(reader);
        Assert.DoesNotContain("ei/after", flowsAfter);
    }

    private static List<string> DrainFlowPaths(System.Threading.Channels.ChannelReader<CaptureEnvelope> reader)
    {
        var paths = new List<string>();
        while (reader.TryRead(out var env))
            if (env.Kind == "flow" && env.Flow is not null)
                paths.Add(env.Flow.Path);
        return paths;
    }

    [Fact]
    public void RingBuffer_CapsAt500_DroppingOldest()
    {
        var hub = new CaptureHub();
        for (int i = 0; i < 600; i++)
            hub.Publish(F("ei/x"), "t");

        var snap = hub.Snapshot();
        Assert.Equal(500, snap.Count);
        Assert.Equal(101, snap[0].Id);
        Assert.Equal(600, snap[^1].Id);
    }

    private const string Yaml = """
routes:
  # ei/
  - path: ei/get_periodicals
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse
  - path: ei_data/log_contract_action
    request: ContractAction
    rawResponse: "SUCCESS"
""";

    private static string MakeRepo() => TestRepoFixture.MakeRepo(Yaml, "ei-dash");

    private static string WrappedResponseB64()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return Convert.ToBase64String(outer.ToByteArray());
    }

    [Fact]
    public void DecodeResponse_KnownType_ReturnsJsonAndKnownType()
    {
        var repo = MakeRepo();
        var decoder = new FlowDecoder(repo);

        var r = decoder.DecodeResponse("ei/get_periodicals", WrappedResponseB64());

        Assert.NotNull(r.Json);
        Assert.Equal("PeriodicalsResponse", r.Type);
        Assert.True(r.Known);
    }

    [Fact]
    public void DecodeResponse_PlainTextAck_SurfacedAsText()
    {
        var repo = MakeRepo();
        var decoder = new FlowDecoder(repo);

        var r = decoder.DecodeResponse(
            "ei_data/log_contract_action",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("SUCCESS")));

        Assert.True(r.Ack);
        Assert.Equal("SUCCESS", r.Text);
        Assert.Null(r.Json);
    }

    [Fact]
    public void DecodeResponse_GarbageBase64_ReturnsNullJson()
    {
        var repo = MakeRepo();
        var decoder = new FlowDecoder(repo);

        var r = decoder.DecodeResponse("ei/get_periodicals", "!!!notbase64");

        Assert.Null(r.Json);
    }

    [Fact]
    public void DecodeRequest_Null_ReturnsNullJson()
    {
        var repo = MakeRepo();
        var decoder = new FlowDecoder(repo);

        Assert.Null(decoder.DecodeRequest("ei/get_periodicals", null).Json);
    }

    [Fact]
    public void StatsSnapshot_FreshHub_CertWaiting_AllZero()
    {
        var hub = new CaptureHub();
        var s = hub.StatsSnapshot();

        Assert.Equal("Waiting", s.CertState);
        Assert.Equal(0, s.ActiveConnections);
        Assert.Equal(0, s.DeviceCount);
        Assert.Empty(s.Devices);
        Assert.Equal(0, s.CapturedAuxbrain);
        Assert.Equal(0, s.Passthrough);
        Assert.Equal(0, s.UniqueEndpoints);
        Assert.Equal(0, s.DecryptOk);
        Assert.Equal(0, s.DecryptErrors);
        Assert.Null(s.LastError);
        Assert.Equal(0, s.BytesCaptured);
        Assert.Null(s.BiggestEndpoint);
        Assert.Equal(0, s.BiggestEndpointBytes);
    }

    [Fact]
    public void RecordConnection_NewIp_TracksDevice_ButStaysWaiting()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "t");

        var s = hub.StatsSnapshot();
        Assert.Equal("Waiting", s.CertState);
        Assert.Equal(1, s.DeviceCount);
        Assert.Equal(1, s.ActiveConnections);
        Assert.Contains(s.Devices, d => d.Ip == "192.168.1.5");
    }

    [Fact]
    public void RecordAuxbrainConnect_FreshHub_StaysWaiting()
    {
        var hub = new CaptureHub();
        hub.RecordAuxbrainConnect();

        Assert.Equal("Waiting", hub.StatsSnapshot().CertState);
    }

    [Fact]
    public void Publish_Auxbrain_FlipsToTrusted_AndCountsCapture()
    {
        var hub = new CaptureHub();
        hub.Publish(F("ei/x"), "t", isAuxbrain: true);

        var s = hub.StatsSnapshot();
        Assert.Equal("Trusted", s.CertState);
        Assert.Equal(1, s.CapturedAuxbrain);
        Assert.Equal(1, s.DecryptOk);
        Assert.Equal(1, s.UniqueEndpoints);
    }

    [Fact]
    public void CertState_DoesNotDowngrade_OnceTrusted()
    {
        var hub = new CaptureHub();
        hub.Publish(F("ei/x"), "t", isAuxbrain: true);
        hub.RecordDecryptError("x", "t");

        var s = hub.StatsSnapshot();
        Assert.Equal("Trusted", s.CertState);
        Assert.Equal(1, s.DecryptErrors);
        Assert.Equal("x", s.LastError);
    }

    [Fact]
    public void RecordDecryptError_AfterConnection_FlipsToUntrusted()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "t");
        hub.RecordDecryptError("boom", "t");

        var s = hub.StatsSnapshot();
        Assert.Equal("Untrusted", s.CertState);
        Assert.Equal(1, s.DecryptErrors);
        Assert.Equal("boom", s.LastError);
    }

    [Fact]
    public void Publish_Passthrough_ReturnsNull_CountsPassthrough_NotBuffered()
    {
        var hub = new CaptureHub();
        var result = hub.Publish(F("ei/x"), "t", isAuxbrain: false);

        Assert.Null(result);
        var s = hub.StatsSnapshot();
        Assert.Equal(1, s.Passthrough);
        Assert.Equal(0, s.CapturedAuxbrain);
        Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public void UniqueEndpoints_CountsDistinctPaths()
    {
        var hub = new CaptureHub();
        hub.Publish(F("ei/a"), "t", isAuxbrain: true);
        hub.Publish(F("ei/a"), "t", isAuxbrain: true);
        hub.Publish(F("ei/b"), "t", isAuxbrain: true);

        Assert.Equal(2, hub.StatsSnapshot().UniqueEndpoints);
    }

    [Fact]
    public void Bytes_TrackedPerEndpoint_BiggestReflectsPath()
    {
        var hub = new CaptureHub();
        hub.Publish(F("ei/big"), "t", isAuxbrain: true);

        var s = hub.StatsSnapshot();
        Assert.True(s.BytesCaptured > 0);
        Assert.Equal("ei/big", s.BiggestEndpoint);
    }

    [Fact]
    public void RecordConnection_SameIpTwice_DeviceCountStaysOne()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "t");
        hub.RecordConnection(1, "192.168.1.5", "t");

        Assert.Equal(1, hub.StatsSnapshot().DeviceCount);
    }

    [Fact]
    public void Device_TracksFirstLastSeenAndConnectionCount()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "10:00:00");
        hub.RecordConnection(2, "192.168.1.5", "10:00:05");

        var d = Assert.Single(hub.StatsSnapshot().Devices);
        Assert.Equal("192.168.1.5", d.Ip);
        Assert.Equal("10:00:00", d.FirstSeen);
        Assert.Equal("10:00:05", d.LastSeen);
        Assert.Equal(2, d.ActiveConnections);
    }

    private static DashboardFlow FlowWithRInfo(string platform, string version) =>
        F("ei/x") with
        {
            RequestJson = $"{{\"rinfo\":{{\"platform\":\"{platform}\",\"version\":\"{version}\"}}}}",
        };

    [Fact]
    public void Device_SingleDevice_GetsOsAndGameVersion()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "t");
        hub.Publish(FlowWithRInfo("IOS", "1.35.6"), "t");

        var d = Assert.Single(hub.StatsSnapshot().Devices);
        Assert.Equal("iOS", d.Os);
        Assert.Equal("1.35.6", d.GameVersion);
    }

    [Fact]
    public void Device_MultipleDevices_NoOsAttribution()
    {
        var hub = new CaptureHub();
        hub.RecordConnection(1, "192.168.1.5", "t");
        hub.RecordConnection(2, "192.168.1.6", "t");
        hub.Publish(FlowWithRInfo("DROID", "1.35.6"), "t");

        Assert.All(hub.StatsSnapshot().Devices, d => { Assert.Null(d.Os); Assert.Null(d.GameVersion); });
    }
}
