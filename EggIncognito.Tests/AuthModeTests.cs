using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

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
    public async Task Callback_404s_WhenWidgetOff()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/auth/callback?code=abc");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task LoginReturn_404s_WhenWidgetOff()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsync("/auth/login-return", new StringContent("\"/admin\""));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
