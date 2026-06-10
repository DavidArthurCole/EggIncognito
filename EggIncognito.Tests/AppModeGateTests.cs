using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Boots the real web host in-process with AppMode=Hosted and proves the capability gate holds at the
// HTTP boundary: capture start is forbidden, read-only tools still work, and the mode endpoint
// reports Hosted. Local-mode behavior is covered by the AppModeService unit tests.
public class AppModeGateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppModeGateTests(WebApplicationFactory<Program> f) =>
        // NoBrowser=true so the test host never tries to launch a real browser (the auto-open is also
        // guarded against the non-Kestrel TestServer in Program, but be explicit here too).
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
