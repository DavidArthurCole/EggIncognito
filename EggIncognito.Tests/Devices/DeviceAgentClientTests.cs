using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class DeviceAgentClientTests {
    private static DeviceAgentClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        var http = new HttpClient(new StubHttpMessageHandler(respond)) { BaseAddress = new Uri("http://runner.local") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["DeviceAgent:Url"] = "http://runner.local",
            ["DeviceAgent:Secret"] = "secret"
        }).Build();
        return new DeviceAgentClient(http, config);
    }

    [Fact]
    public void Enabled_UrlAndSecretSet_True() {
        var client = Client(_ => throw new HttpRequestException("unused"));
        Assert.True(client.Enabled);
    }

    [Fact]
    public async Task ProbeAsync_TransportFailure_ReturnsNull() {
        var client = Client(_ => throw new HttpRequestException("connection refused"));
        var result = await client.ProbeAsync("frame-android", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ProbeAllAsync_TransportFailure_ReturnsZero() {
        var client = Client(_ => throw new HttpRequestException("connection refused"));
        int n = await client.ProbeAllAsync(CancellationToken.None);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task ProbeAsync_CallerCancelled_PropagatesCancellation() {
        using var cts = new CancellationTokenSource();
        var client = Client(_ => {
            cts.Token.ThrowIfCancellationRequested();
            throw new HttpRequestException("unused");
        });
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ProbeAsync("frame-android", cts.Token));
    }
}
