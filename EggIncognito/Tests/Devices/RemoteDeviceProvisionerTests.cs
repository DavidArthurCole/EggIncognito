using System.Net;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class RemoteDeviceProvisionerTests {
    private const string InstanceJson =
        "{\"instanceId\":\"egi-vd-ab12\",\"kind\":\"redroid\",\"image\":\"redroid/redroid:12\","
        + "\"state\":\"ready\",\"adbSerial\":\"172.17.0.4:5555\",\"hostRef\":\"deadbeef\","
        + "\"createdAt\":\"2026-08-29T10:00:00+00:00\",\"note\":\"attached to docker network egi\"}";

    private static DeviceTransportConfig Config(
        DeviceTransportMode mode = DeviceTransportMode.Remote,
        string? baseUrl = "https://frame.test",
        string? apiKey = "k") => new() {
            Mode = mode,
            RemoteBaseUrl = baseUrl,
            ApiKey = apiKey
        };

    private static RemoteDeviceProvisioner Provisioner(
        Func<HttpRequestMessage, HttpResponseMessage> respond, DeviceTransportConfig? config = null) =>
        new(new StubHttpFactory(new StubHttpMessageHandler(respond)), config ?? Config());

    private static RemoteDeviceProvisioner Refusing(DeviceTransportConfig config) =>
        Provisioner(_ => throw new InvalidOperationException("no request should be sent"), config);

    [Fact]
    public void Kind_IsRemote() =>
        Assert.Equal("remote", Provisioner(_ => new HttpResponseMessage(HttpStatusCode.OK)).Kind);

    [Theory]
    [InlineData("remote", true)]
    [InlineData("Remote", true)]
    [InlineData("REMOTE", true)]
    [InlineData("redroid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRemoteKind_MatchesCaseInsensitively(string? kind, bool expected) =>
        Assert.Equal(expected, RemoteDeviceProvisioner.IsRemoteKind(kind));

    [Fact]
    public void Capabilities_CoverCreateDestroyListOnly() {
        var caps = Provisioner(_ => new HttpResponseMessage(HttpStatusCode.OK)).Capabilities;

        Assert.True(caps.HasFlag(ProvisionerCapabilities.Create));
        Assert.True(caps.HasFlag(ProvisionerCapabilities.Destroy));
        Assert.True(caps.HasFlag(ProvisionerCapabilities.List));
        Assert.False(caps.HasFlag(ProvisionerCapabilities.StartStop));
    }

    [Fact]
    public void ConfigurationNote_FullyConfigured_IsNull() =>
        Assert.Null(Provisioner(_ => new HttpResponseMessage(HttpStatusCode.OK)).ConfigurationNote);

    [Fact]
    public void ConfigurationNote_TransportModeLocal_NamesTheMisconfiguration() {
        var config = Config(mode: DeviceTransportMode.Local);

        string? note = Refusing(config).ConfigurationNote;

        Assert.NotNull(note);
        Assert.Contains("Devices:Virtual:Kind", note, StringComparison.Ordinal);
        Assert.Contains("DeviceTransport:Mode", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationNote_MissingBaseUrl_NamesTheKey() {
        var config = Config(baseUrl: null);

        string? note = Refusing(config).ConfigurationNote;

        Assert.NotNull(note);
        Assert.Contains("RemoteBaseUrl", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationNote_MissingApiKey_NamesTheKey() {
        var config = Config(apiKey: "");

        string? note = Refusing(config).ConfigurationNote;

        Assert.NotNull(note);
        Assert.Contains("ApiKey", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_TransportModeLocal_IsUnsupportedAndSendsNothing() {
        var provisioner = Refusing(Config(mode: DeviceTransportMode.Local));

        var result = await provisioner.CreateAsync(new ProvisionSpec("remote", ""), CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains("DeviceTransport:Mode", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_MissingBaseUrl_IsUnsupportedAndSendsNothing() {
        var provisioner = Refusing(Config(baseUrl: null));

        var result = await provisioner.ListAsync(CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains("RemoteBaseUrl", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DestroyAsync_MissingApiKey_IsUnsupportedAndSendsNothing() {
        var provisioner = Refusing(Config(apiKey: null));

        var result = await provisioner.DestroyAsync("egi-vd-ab12", CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains("ApiKey", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_Success_PostsToBridgeAndMapsInstance() {
        HttpRequestMessage? seen = null;
        string? body = null;
        var provisioner = Provisioner(req => {
            seen = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"ok\":true,\"outcome\":\"ok\",\"note\":\"created\",\"instance\":" + InstanceJson + "}");
        });

        var result = await provisioner.CreateAsync(
            new ProvisionSpec("remote", "redroid/redroid:12"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("https://frame.test/api/devices/virtual/bridge/create", seen.RequestUri!.ToString());
        Assert.Equal("k", seen.Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("redroid/redroid:12", body!, StringComparison.Ordinal);
        Assert.Equal("egi-vd-ab12", result.Value!.InstanceId);
        Assert.Equal("redroid", result.Value.Kind);
        Assert.Equal(ProvisionStates.Ready, result.Value.State);
        Assert.Equal("172.17.0.4:5555", result.Value.AdbSerial);
    }

    [Fact]
    public async Task CreateAsync_ProdRefuses_IsErrorCarryingTheRemoteNote() {
        var provisioner = Provisioner(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"ok\":false,\"outcome\":\"error\",\"note\":\"virtual device cap reached (4/4)\"}"));

        var result = await provisioner.CreateAsync(new ProvisionSpec("remote", ""), CancellationToken.None);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
        Assert.Equal("virtual device cap reached (4/4)", result.Note);
    }

    [Fact]
    public async Task CreateAsync_BridgeOff_IsUnreachable() {
        var provisioner = Provisioner(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await provisioner.CreateAsync(new ProvisionSpec("remote", ""), CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Contains("404", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_TransportThrows_IsUnreachableNotThrow() {
        var provisioner = Provisioner(_ => throw new HttpRequestException("connection refused"));

        var result = await provisioner.CreateAsync(new ProvisionSpec("remote", ""), CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Contains("connection refused", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_OkWithNoInstance_IsError() {
        var provisioner = Provisioner(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"ok\":true,\"outcome\":\"ok\",\"note\":null,\"instance\":null}"));

        var result = await provisioner.CreateAsync(new ProvisionSpec("remote", ""), CancellationToken.None);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
    }

    [Fact]
    public async Task ListAsync_Success_GetsInstancesFromBridge() {
        HttpRequestMessage? seen = null;
        var provisioner = Provisioner(req => {
            seen = req;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"ok\":true,\"outcome\":\"ok\",\"note\":null,\"instances\":[" + InstanceJson + "]}");
        });

        var result = await provisioner.ListAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Equal("https://frame.test/api/devices/virtual/bridge/instances", seen.RequestUri!.ToString());
        Assert.Equal("egi-vd-ab12", Assert.Single(result.Value!).InstanceId);
    }

    [Fact]
    public async Task ListAsync_TransportThrows_IsUnreachableNotThrow() {
        var provisioner = Provisioner(_ => throw new HttpRequestException("dns failure"));

        var result = await provisioner.ListAsync(CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DestroyAsync_Success_PostsInstanceScopedVerb() {
        HttpRequestMessage? seen = null;
        var provisioner = Provisioner(req => {
            seen = req;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                "{\"ok\":true,\"outcome\":\"ok\",\"note\":\"destroyed by admin\"}");
        });

        var result = await provisioner.DestroyAsync("egi-vd-ab12", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("https://frame.test/api/devices/virtual/bridge/egi-vd-ab12/destroy",
            seen.RequestUri!.ToString());
    }

    [Fact]
    public async Task DestroyAsync_ProdRefuses_IsErrorCarryingTheRemoteNote() {
        var provisioner = Provisioner(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"ok\":false,\"outcome\":\"error\",\"note\":\"device job 'harvest' is running\"}"));

        var result = await provisioner.DestroyAsync("egi-vd-ab12", CancellationToken.None);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
        Assert.Equal("device job 'harvest' is running", result.Note);
    }

    [Fact]
    public async Task DestroyAsync_NotAllowlisted_IsUnreachable() {
        var provisioner = Provisioner(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await provisioner.DestroyAsync("egi-vd-ab12", CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Contains("403", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAndStop_AreUnsupported() {
        var provisioner = Refusing(Config());

        var started = await provisioner.StartAsync("egi-vd-ab12", CancellationToken.None);
        var stopped = await provisioner.StopAsync("egi-vd-ab12", CancellationToken.None);

        Assert.Equal(DeviceOutcome.Unsupported, started.Outcome);
        Assert.Equal(DeviceOutcome.Unsupported, stopped.Outcome);
    }
}
