using System.Net;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Feed;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// Dispatcher fan-out over a faked IFeedSubscriptionStore (no EF) + a stub IHttpClientFactory. Covers:
// proto_changed fires on change and is skipped when unchanged, 410 deactivates, idempotent re-delivery.
public class FeedDispatcherTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Posts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Posts++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    // In-memory store fake: holds subs + records deliveries, mirrors the real surface the dispatcher uses.
    private sealed class FakeStore(params FeedSubscription[] subs) : IFeedSubscriptionStore
    {
        public List<FeedSubscription> Subs { get; } = [.. subs];
        public List<FeedDelivery> Deliveries { get; } = [];

        public Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default)
        {
            Subs.Add(sub);
            return Task.FromResult(sub);
        }

        public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.Active).ToList());

        public Task<bool> AlreadyDeliveredAsync(int subId, int protoVersionId, CancellationToken ct = default) =>
            Task.FromResult(Deliveries.Any(d => d.SubscriptionId == subId && d.ProtoVersionId == protoVersionId));

        public Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default)
        {
            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(int subId, bool active, CancellationToken ct = default)
        {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            s?.Active = active;
            return Task.CompletedTask;
        }

        public Task BumpFailAsync(int subId, CancellationToken ct = default)
        {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) s.FailCount++;
            return Task.CompletedTask;
        }

        public Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default)
        {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) { s.LastDeliveryAt = at; s.FailCount = 0; }
            return Task.CompletedTask;
        }

        public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.OwnerUserId == ownerUserId)
                .OrderByDescending(s => s.CreatedAt).ToList());

        public Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default)
        {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            Subs.Remove(s);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger,
            bool active, string? messageTemplate, CancellationToken ct = default)
        {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            s.Platforms = platforms;
            s.Trigger = trigger;
            s.Active = active;
            s.MessageTemplate = messageTemplate;
            return Task.FromResult(true);
        }
    }

    private static FeedDispatcher Dispatcher(FakeStore store, HttpMessageHandler handler) =>
        new(store, new StubHttpFactory(handler), NullLogger<FeedDispatcher>.Instance);

    private static FeedSubscription Sub(int id, string trigger, params string[] platforms) => new()
    {
        Id = id, Kind = "discord", TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger, Platforms = platforms, Active = true,
    };

    [Fact]
    public async Task ProtoChanged_Fires_OnChange()
    {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(
            7, "android", "1.0", "111343", "72", "sha", created: true, protoChanged: true, "https://x/y");

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal("sent", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task ProtoChanged_Skipped_WhenUnchanged()
    {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(
            7, "android", "1.0", "111343", "72", "sha", created: true, protoChanged: false, "https://x/y");

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Gone410_Deactivates()
    {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Gone));
        await Dispatcher(store, handler).DispatchAsync(
            7, "android", "1.0", "111343", "72", "sha", created: true, protoChanged: false, "https://x/y");

        Assert.False(store.Subs[0].Active);
        Assert.Equal("failed", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task Idempotent_SecondEvent_NoSecondDelivery()
    {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(7, "android", "1.0", "111343", "72", "sha", true, false, "https://x/y");
        await d.DispatchAsync(7, "android", "1.0", "111343", "72", "sha", true, false, "https://x/y");

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
    }

    [Fact]
    public async Task CustomTemplate_SendsRenderedContent_NotEmbed()
    {
        var sub = Sub(1, "proto_changed", "android");
        sub.MessageTemplate = "New build {{appVersion}} ({{build}}) on {{platform}}: {{protoChanged}}";
        var store = new FakeStore(sub);
        string? sentBody = null;
        var handler = new StubHandler(req => { sentBody = req.Content!.ReadAsStringAsync().Result; return new HttpResponseMessage(HttpStatusCode.NoContent); });
        await Dispatcher(store, handler).DispatchAsync(
            7, "android", "1.0", "111343", "72", "sha", created: true, protoChanged: true, "https://x/y");

        Assert.Contains("New build 1.0 (111343) on android: changed", sentBody);
        Assert.DoesNotContain("embeds", sentBody);
    }
}
