using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class AppVersionTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public AppVersionTests(SharedAppFactory f) => _factory = f;

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
