using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EggIncognito.Tests;

// Boots the real web host in-process and proves the sync endpoint's auth ladder against the
// SyncKit.Bot.NewVersionHandler-backed route: 401 on missing/bad bearer, 200 on a good authed
// request. No more 202/outcome-body assertions - NewVersionHandler always returns a bare 200.
public class EventsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "test-secret-123";
    private const string Body = "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"protoSha\":\"x\"}";

    private readonly WebApplicationFactory<Program> _base;

    public EventsControllerTests(WebApplicationFactory<Program> f) => _base = f;

    private HttpClient Client(bool withSecret) =>
        _base.WithWebHostBuilder(b =>
        {
            b.UseSetting("NoBrowser", "true");
            if (withSecret) b.UseSetting("SyncEvent:EventSecret", Secret);
        }).CreateClient();

    private static StringContent Json() => new(Body, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task NoSecretConfigured_Is404()
    {
        var c = Client(withSecret: false);
        var r = await c.PostAsync("/events/new-version", Json());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task NoBearer_Is401()
    {
        var c = Client(withSecret: true);
        var r = await c.PostAsync("/events/new-version", Json());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task WrongBearer_Is401()
    {
        var c = Client(withSecret: true);
        var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task RightBearer_Is200()
    {
        var c = Client(withSecret: true);
        var req = new HttpRequestMessage(HttpMethod.Post, "/events/new-version") { Content = Json() };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
