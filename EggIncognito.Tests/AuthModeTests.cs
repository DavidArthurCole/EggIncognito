using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// No Discord creds + no DB in the test host, so auth is NOT wired: the app stays fully anonymous and
// the mode endpoint reports authEnabled:false. Proves the no-regression anonymous path.
public class AuthModeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AuthModeTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Mode_ReportsAuthDisabled_WhenNoCreds()
    {
        var c = _factory.CreateClient();
        var json = await c.GetStringAsync("/api/app/mode");
        Assert.Contains("\"authEnabled\":false", json);
        Assert.Contains("\"user\":null", json);
    }

    [Fact]
    public async Task Login_404s_WhenAuthOff()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/login");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Me_ReportsUnauthenticated()
    {
        var c = _factory.CreateClient();
        var json = await c.GetStringAsync("/api/auth/me");
        Assert.Contains("\"authenticated\":false", json);
    }
}
