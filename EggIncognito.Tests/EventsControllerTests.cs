using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Boots the real web host in-process and proves the sync endpoint's auth ladder:
// 404 without a configured secret, 401 on a missing/bad bearer, 202 on a good authed request in any
// AppMode (no longer hosted-gated). Mirrors AppModeGateTests' WebApplicationFactory pattern.
public class EventsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "test-secret-123";
    private const string Body = "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"protoSha\":\"x\"}";

    private readonly WebApplicationFactory<Program> _base;

    public EventsControllerTests(WebApplicationFactory<Program> f) => _base = f;

    private HttpClient Client(string appMode, bool withSecret) =>
        _base.WithWebHostBuilder(b =>
        {
            b.UseSetting("AppMode", appMode);
            b.UseSetting("NoBrowser", "true");
            if (withSecret) b.UseSetting("SyncEvent:EventSecret", Secret);
        }).CreateClient();

    private static StringContent Json() => new(Body, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task NoSecretConfigured_Is404()
    {
        var c = Client("Local", withSecret: false);
        var r = await c.PostAsync("/events/new-version", Json());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task NoBearer_Is401()
    {
        var c = Client("Local", withSecret: true);
        var r = await c.PostAsync("/events/new-version", Json());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task WrongBearer_Is401()
    {
        var c = Client("Local", withSecret: true);
        var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Hosted_RightBearer_Is202()
    {
        // A correctly-authed event is accepted regardless of AppMode.
        var c = Client("Hosted", withSecret: true);
        var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
    }

    [Fact]
    public async Task Local_RightBearer_Is202()
    {
        var c = Client("Local", withSecret: true);
        var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
    }
}
