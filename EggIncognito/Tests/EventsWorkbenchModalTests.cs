using System.Net;
using System.Text.Json;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Events;
using EggIncognito.Models.Contracts;
using EggIncognito.Models.Events;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class EventsWorkbenchModalTests : BunitContext {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private void Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(new StubFactory(respond));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        Services.AddSingleton<ICurrentUser>(new FakeUser(UserRole.Viewer));
        Services.AddScoped<EventsWorkbenchState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static GameEventDto Event(string id, string message, DateTimeOffset start, DateTimeOffset end, bool ultra = false) =>
        new(id, "earnings-boost", message, 2, ultra, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end), "device");

    private static HttpResponseMessage Ok(params GameEventDto[] events) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new GameEventListResponse(events.Length, events), Web));

    private static HttpResponseMessage NoContracts() =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new ContractReleaseListResponse(0, []), Web));

    private static HttpResponseMessage Respond(HttpRequestMessage req, params GameEventDto[] events) =>
        req.RequestUri?.AbsolutePath.StartsWith("/api/v1/contracts", StringComparison.Ordinal) == true
            ? NoContracts()
            : Ok(events);

    private async Task<IRenderedComponent<EventsWorkbenchModal>> OpenAsync() {
        var cut = Render<EventsWorkbenchModal>();
        await cut.InvokeAsync(() => cut.Instance.Open());
        return cut;
    }

    [Fact]
    public async Task Window_RendersOneBarPerEventWithItsMessage() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        var bars = cut.FindAll(".evcal-bar");
        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.Contains("Earnings boost", bar.TextContent));
    }

    [Fact]
    public async Task OverlappingEvents_StackIntoSeparateLanes() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req,
            Event("a", "First", now.AddHours(-2), now.AddHours(2)),
            Event("b", "Second", now.AddHours(-1), now.AddHours(3))));

        var cut = await OpenAsync();

        var rowsWithBars = cut.FindAll(".evcal-row")
            .Where(row => row.QuerySelectorAll(".evcal-bar").Length > 0)
            .ToList();
        Assert.NotEmpty(rowsWithBars);
        Assert.Contains(rowsWithBars, row => row.QuerySelectorAll(".evcal-lane").Length == 2);
        Assert.All(rowsWithBars, row => Assert.All(
            row.QuerySelectorAll(".evcal-lane"),
            lane => Assert.True(lane.QuerySelectorAll(".evcal-bar").Length <= 1)));
    }

    [Fact]
    public async Task NowLine_IsDrawnOnceWhenNowFallsInsideTheWindow() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        Assert.Single(cut.FindAll(".evcal-now"));
    }

    [Fact]
    public async Task UltraEvent_UsesTheCcGradientAndSprite() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Ultra sale", now.AddHours(-2), now.AddHours(2), true)));

        var cut = await OpenAsync();

        var style = cut.Find(".evcal-bar").GetAttribute("style") ?? "";
        Assert.Contains(EventPalette.CcGradientFrom, style, StringComparison.Ordinal);
        Assert.Contains(EventPalette.CcGradientTo, style, StringComparison.Ordinal);
        Assert.Contains("cc=1", cut.Find(".evcal-bar-icon").GetAttribute("src") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEvent_UsesTheTypeColorAndPlainSprite() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        var style = cut.Find(".evcal-bar").GetAttribute("style") ?? "";
        Assert.Contains(EventPalette.ColorFor("earnings-boost"), style, StringComparison.Ordinal);
        Assert.DoesNotContain("cc=1", cut.Find(".evcal-bar-icon").GetAttribute("src") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseLessInstance_ShowsAShortNoteAndNoBars() {
        Wire(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = await OpenAsync();

        Assert.Contains("needs a database", cut.Find(".evcal-note").TextContent);
        Assert.Empty(cut.FindAll(".evcal-bar"));
    }

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory {
        public HttpClient CreateClient(string name) =>
            new(new StubHttpMessageHandler(respond)) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => role != UserRole.Viewer;
        public Guid? UserId => null;
        public string? DiscordId => null;
        public string? Username => null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}
