using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class AuthModeTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public AuthModeTests(SharedAppFactory f) => _factory = f;

    [Fact]
    public async Task Mode_ReportsAuthDisabled_WhenNoCreds()
    {
        var c = _factory.CreateClient();
        var json = await c.GetStringAsync("/api/app/mode");
        Assert.Contains("\"authEnabled\":false", json);
        Assert.Contains("\"user\":null", json);
    }

    [Fact]
    public async Task Logout_404s_WhenAuthOff()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.PostAsync("/logout", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Me_ReportsUnauthenticated()
    {
        var c = _factory.CreateClient();
        var json = await c.GetStringAsync("/api/auth/me");
        Assert.Contains("\"authenticated\":false", json);
    }

    [Fact]
    public async Task Code_PassesThrough_WhenAuthOff()
    {
        // No callback middleware is registered when auth is off, so ?code is not intercepted.
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/health?code=abc");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
