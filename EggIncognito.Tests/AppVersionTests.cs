using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class AppVersionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AppVersionTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task AppVersion_ReturnsVersionAndSha()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/app/version");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"sha\"", json);
    }

    [Fact]
    public async Task ReconnectWatcher_ScriptIsServed()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/interop/reconnectWatcher.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("location.reload", body);
        Assert.Contains("/api/app/version", body);
    }
}
