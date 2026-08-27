using System.Net;
using System.Text.Json;
using Bunit;
using EggIncognito.Components.Protos;
using EggIncognito.Models.Events;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class EventsCalendarModalTests : BunitContext {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private void Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(new StubFactory(respond));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static GameEventDto Event(string id, string message, DateTimeOffset start, DateTimeOffset end, bool ultra = false) =>
        new(id, "earnings-boost", message, 2, ultra, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end), "device");

    private static HttpResponseMessage Ok(params GameEventDto[] events) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new GameEventListResponse(events.Length, events), Web));

    private async Task<IRenderedComponent<EventsCalendarModal>> OpenAsync() {
        var cut = Render<EventsCalendarModal>();
        await cut.InvokeAsync(() => cut.Instance.Open());
        return cut;
    }

    [Fact]
    public async Task Window_RendersOneBarPerEventWithItsMessage() {
        var now = DateTimeOffset.UtcNow;
        Wire(_ => Ok(Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        var bars = cut.FindAll(".evcal-bar");
        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.Contains("Earnings boost", bar.TextContent));
    }

    [Fact]
    public async Task OverlappingEvents_StackIntoSeparateLanes() {
        var now = DateTimeOffset.UtcNow;
        Wire(_ => Ok(
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
        Wire(_ => Ok(Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        Assert.Single(cut.FindAll(".evcal-now"));
    }

    [Fact]
    public async Task UltraEvent_GetsAnInlineMarker() {
        var now = DateTimeOffset.UtcNow;
        Wire(_ => Ok(Event("a", "Ultra sale", now.AddHours(-2), now.AddHours(2), true)));

        var cut = await OpenAsync();

        Assert.NotEmpty(cut.FindAll(".evcal-ultra"));
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
}
