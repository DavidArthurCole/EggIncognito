using System.Net;
using EggIncognito.Controllers;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using EggIdentity.Contract;

namespace EggIncognito.Tests;

public class ProtoFeedApiTests {
    private static FeedSubscriptionStore UnconnectedStore() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        return new FeedSubscriptionStore(new EggIncognitoDbContext(opts));
    }

    private static ProtoFeedController Controller(IServiceProvider services,
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(services, new StubHttpFactory(new StubHandler(respond)));

    private static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode ?? 200;

    [Fact]
    public async Task Create_NoStore_Returns503() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq(
            "https://discord.com/api/webhooks/1/abc", null, null, null, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Create_BadUrl_Returns400() {
        var services = new MapServices(new Dictionary<Type, object?> { [typeof(FeedSubscriptionStore)] = UnconnectedStore() });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq(
            "https://evil.example.com/hook", null, null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Create_EmptyUrl_Returns400() {
        var services = new MapServices(new Dictionary<Type, object?> { [typeof(FeedSubscriptionStore)] = UnconnectedStore() });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new ProtoFeedController.CreateReq("", null, null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Mine_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Mine(CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Mine_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Mine(CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Delete_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Delete(1, CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Delete_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Delete(1, CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Update_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Update(1, new ProtoFeedController.UpdateReq(["android"], "new_version", true, null),
            CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Update_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Update(1, new ProtoFeedController.UpdateReq(null, null, null, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/abcdEFGHtoken1234", "webhooks/123456789/...1234")]
    [InlineData("https://discord.com/api/webhooks/999/tok", "webhooks/999/...tok")]
    public void MaskWebhook_DiscordUrl_ShowsIdHidesToken(string url, string expected) {
        string masked = ProtoFeedController.MaskWebhook(url);
        Assert.Equal(expected, masked);

        Assert.DoesNotContain("abcdEFGHtoken1234", masked);
    }

    [Fact]
    public void MaskWebhook_NonDiscord_GenericTail() {
        string masked = ProtoFeedController.MaskWebhook("https://example.com/hook/SECRETvalue");
        Assert.Equal("...Tvalue", masked);
        Assert.DoesNotContain("SECRETvalue", masked);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class MapServices(Dictionary<Type, object?> map) : IServiceProvider {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }

    private sealed class StubUser(string? discordId) : ICurrentUser {
        public bool IsAuthenticated => discordId is not null;
        public Guid? UserId => discordId is not null ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        public string? DiscordId => discordId;
        public string? Username => null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => false;
    }
}
