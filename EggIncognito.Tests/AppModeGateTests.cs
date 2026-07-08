using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class AppModeGateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppModeGateTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b
            .UseSetting("AppMode", "Hosted")
            .UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Hosted_CaptureStart_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/api/capture/start", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Theory]
    [InlineData("/api/capture/stream")]
    [InlineData("/api/capture/flows")]
    [InlineData("/api/capture/stats")]
    [InlineData("/api/capture/decode?path=ei/first_contact&responseB64=AA==")]
    public async Task Hosted_CaptureReads_Are403(string path)
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Hosted_ToolsDecode_Works()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/tools/decode", new { base64 = "" });
        Assert.True(r.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Mode_ReportsHosted()
    {
        var c = _factory.CreateClient();
        var json = await c.GetStringAsync("/api/app/mode");
        Assert.Contains("Hosted", json);
        Assert.Contains("\"canWrite\":false", json);
    }
}
