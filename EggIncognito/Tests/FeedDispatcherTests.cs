using System.Net;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Feed;
using EggIncognito.Services.ProtoExtract;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class FeedDispatcherTests {
    private static FeedDispatcher Dispatcher(FakeStore store, HttpMessageHandler handler) =>
        new(store, new StubHttpFactory(handler), NullLogger<FeedDispatcher>.Instance);

    private static FeedSubscription Sub(int id, string trigger, params string[] platforms) => new() {
        Id = id,
        Kind = "discord",
        EventKind = "proto_build",
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = platforms,
        Active = true
    };

    private static FeedSubscription PeriodicalsSub(int id, string trigger) => new() {
        Id = id,
        Kind = "discord",
        EventKind = "periodicals_changed",
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = [],
        Active = true
    };

    private static ProtoBuildEvent ProtoEvt(bool protoChanged, string platform = "android", int id = 7) =>
        new(id, platform, "1.0", "111343", "72", "sha", true, protoChanged, "https://x/y");

    [Fact]
    public async Task ProtoChanged_Fires_OnChange() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal("sent", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task ProtoChanged_Skipped_WhenUnchanged() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Gone410_Deactivates() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Gone));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.False(store.Subs[0].Active);
        Assert.Equal("failed", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task Idempotent_SecondEvent_NoSecondDelivery() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ProtoEvt(false));
        await d.DispatchAsync(ProtoEvt(false));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
    }

    [Fact]
    public async Task CustomTemplate_SendsRenderedContent_NotEmbed() {
        var sub = Sub(1, "proto_changed", "android");
        sub.MessageTemplate = "New build {{appVersion}} ({{build}}) on {{platform}}: {{protoChanged}}";
        var store = new FakeStore(sub);
        string? sentBody = null;
        var handler = new StubHandler(req => {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Contains("New build 1.0 (111343) on android: changed", sentBody);
        Assert.DoesNotContain("embeds", sentBody);
    }

    [Fact]
    public async Task WrongKind_Subscription_NotFired() {
        var store = new FakeStore(PeriodicalsSub(1, "any"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Periodicals_Any_Fires_ProtoSubIgnored() {
        var store = new FakeStore(PeriodicalsSub(1, "any"), Sub(2, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler)
            .DispatchAsync(new PeriodicalsChangedEvent("periodicals", "abc123", "https://x/periodicals"));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal("periodicals_changed", store.Deliveries[0].EventKind);
        Assert.Equal("periodicals:abc123", store.Deliveries[0].DedupKey);
    }

    [Fact]
    public async Task Periodicals_FeedTrigger_MatchesOnlyThatFeed() {
        var store = new FakeStore(PeriodicalsSub(1, "afx-config"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(new PeriodicalsChangedEvent("periodicals", "h1", "https://x/periodicals"));
        Assert.Equal(0, handler.Posts);
        await d.DispatchAsync(new PeriodicalsChangedEvent("afx-config", "h2", "https://x/periodicals"));
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Periodicals_SameContentHash_Deduped() {
        var store = new FakeStore(PeriodicalsSub(1, "any"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(new PeriodicalsChangedEvent("periodicals", "samehash", "https://x/periodicals"));
        await d.DispatchAsync(new PeriodicalsChangedEvent("periodicals", "samehash", "https://x/periodicals"));

        Assert.Equal(1, handler.Posts);
        await d.DispatchAsync(new PeriodicalsChangedEvent("periodicals", "newhash", "https://x/periodicals"));
        Assert.Equal(2, handler.Posts);
    }

    private static FeedSubscription Guarded(int id, string trigger, params string[] platforms) {
        var sub = Sub(id, trigger, platforms);
        sub.Filters = [
            FeedEventKinds.FilterRequireClientVersion, FeedEventKinds.FilterRequireProto,
            FeedEventKinds.FilterSaneBuild, FeedEventKinds.FilterKnownDelta
        ];
        return sub;
    }


    private static ProtoBuildEvent BrokenIosEvt(int id = 42) {
        var flaws = ProtoVersionQuality.Flaws("ios", "1.36.4", "111340", null, "", false);
        return new ProtoBuildEvent(id, "ios", "1.36.4", "111340", null, "", true, true, "https://x/y",
            VersionDelta.Unknown, "1.37.0", "1.37.0.1", flaws);
    }


    [Fact]
    public async Task VersionUp_Fires_OnForward_Only() {
        var store = new FakeStore(Sub(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Backfill, ProtoVersionId = 1 });
        Assert.Equal(0, handler.Posts);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Forward, ProtoVersionId = 2 });
        Assert.Equal(1, handler.Posts);
    }


    [Fact]
    public async Task Filters_Block_Flawed_Event_And_Record_Reason() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerNewVersion, "ios"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(BrokenIosEvt());

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterRequireClientVersion, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterSaneBuild, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterKnownDelta, blocked.Reason, StringComparison.Ordinal);
    }


    [Fact]
    public async Task Suspect_Fires_OnFlawed_NotOnClean() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerSuspect, "android", "ios"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true, id: 5) with { Delta = VersionDelta.Forward, Flaws = [] });
        Assert.Equal(0, handler.Posts);

        await d.DispatchAsync(BrokenIosEvt());
        Assert.Equal(1, handler.Posts);
        Assert.Empty(store.Suppressions);
    }


    [Fact]
    public async Task Clean_Forward_Event_Passes_Filters() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(
            ProtoEvt(true) with { Delta = VersionDelta.Forward, Flaws = [] });

        Assert.Equal(1, handler.Posts);
        Assert.Empty(store.Suppressions);
    }


    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public int Posts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Posts++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeStore(params FeedSubscription[] subs) : IFeedSubscriptionStore {
        public List<FeedSubscription> Subs { get; } = [.. subs];
        public List<FeedDelivery> Deliveries { get; } = [];
        public List<FeedSuppression> Suppressions { get; } = [];

        public Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default) {
            Subs.Add(sub);
            return Task.FromResult(sub);
        }

        public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.Active).ToList());

        public Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey,
            CancellationToken ct = default) =>
            Task.FromResult(Deliveries.Any(d =>
                d.SubscriptionId == subId && d.EventKind == eventKind && d.DedupKey == dedupKey));

        public Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default) {
            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(int subId, bool active, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            s?.Active = active;
            return Task.CompletedTask;
        }

        public Task BumpFailAsync(int subId, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) s.FailCount++;
            return Task.CompletedTask;
        }

        public Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) {
                s.LastDeliveryAt = at;
                s.FailCount = 0;
            }

            return Task.CompletedTask;
        }

        public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.OwnerUserId == ownerUserId)
                .OrderByDescending(s => s.CreatedAt).ToList());

        public Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            Subs.Remove(s);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger,
            bool active, string? messageTemplate, string[] filters, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            s.Platforms = platforms;
            s.Trigger = trigger;
            s.Active = active;
            s.MessageTemplate = messageTemplate;
            s.Filters = filters;
            return Task.FromResult(true);
        }

        public Task SuppressAsync(int subId, string eventKind, string dedupKey, string reason, string? summary,
            CancellationToken ct = default) {
            Suppressions.Add(new FeedSuppression {
                SubscriptionId = subId,
                EventKind = eventKind,
                DedupKey = dedupKey,
                Reason = reason,
                Summary = summary
            });
            return Task.CompletedTask;
        }
    }
}
