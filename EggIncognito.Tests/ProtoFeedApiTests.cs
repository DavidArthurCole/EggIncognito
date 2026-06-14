using System.Net;
using EggIncognito.Controllers;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

// Unit-level coverage of the create endpoint over a stub IServiceProvider + IHttpClientFactory, so the
// shape is asserted without a live DB. The 503 path runs with no FeedSubscriptionStore in the provider.
// The bad/empty-URL 400 paths short-circuit before any store query, so a never-connected store stands in;
// they hold whether or not a DB is configured.
public class ProtoFeedApiTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MapServices(Dictionary<Type, object?> map) : IServiceProvider
    {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }

    // Never opens a connection; the 400 paths return before any query touches it.
    private static FeedSubscriptionStore UnconnectedStore()
    {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        return new FeedSubscriptionStore(new EggIncognitoDbContext(opts));
    }

    private static ProtoFeedController Controller(IServiceProvider services,
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(services, new StubHttpFactory(new StubHandler(respond)));

    private static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode ?? 200;

    [Fact]
    public async Task Create_NoStore_Returns503()
    {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq(
            "https://discord.com/api/webhooks/1/abc", null, null, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Create_BadUrl_Returns400()
    {
        var services = new MapServices(new() { [typeof(FeedSubscriptionStore)] = UnconnectedStore() });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq(
            "https://evil.example.com/hook", null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Create_EmptyUrl_Returns400()
    {
        var services = new MapServices(new() { [typeof(FeedSubscriptionStore)] = UnconnectedStore() });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq("", null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
    }
}
