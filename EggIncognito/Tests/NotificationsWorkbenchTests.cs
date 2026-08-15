using EggIncognito.Data.Models;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Notifications;

namespace EggIncognito.Tests;

public class NotificationsWorkbenchTests {
    private static FeedSubscription Probe(string kind, string trigger, params string[] filters) => new() {
        EventKind = kind,
        Trigger = trigger,
        Platforms = ["android", "ios"],
        Filters = filters
    };

    private static readonly string[] ProtoGuards = [
        FeedEventKinds.FilterRequireClientVersion, FeedEventKinds.FilterRequireProto,
        FeedEventKinds.FilterSaneBuild, FeedEventKinds.FilterKnownDelta
    ];

    [Fact]
    public void EveryKind_HasSamples() {
        foreach (var kind in FeedEventKinds.All)
            Assert.NotEmpty(FeedSamples.For(kind.Key));
    }

    [Fact]
    public void UnknownKind_HasNoSamples() => Assert.Empty(FeedSamples.For("not_a_kind"));

    [Fact]
    public void BrokenSample_BlockedByDefaultGuards() {
        var broken = FeedSamples.Find(FeedEventKinds.ProtoBuild, "broken");
        Assert.NotNull(broken);

        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerNewVersion, ProtoGuards);
        Assert.True(broken.Event.Matches(sub));
        Assert.NotEmpty(broken.Event.BlockedBy(sub));
    }

    [Fact]
    public void BrokenSample_ReachesSuspect() {
        var broken = FeedSamples.Find(FeedEventKinds.ProtoBuild, "broken");
        Assert.NotNull(broken);

        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerSuspect, ProtoGuards);
        Assert.True(broken.Event.Matches(sub));
        Assert.Empty(broken.Event.BlockedBy(sub));
    }

    [Fact]
    public void ForwardSample_PassesGuardsOnVersionUp() {
        var forward = FeedSamples.Find(FeedEventKinds.ProtoBuild, "forward");
        Assert.NotNull(forward);

        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ProtoGuards);
        Assert.True(forward.Event.Matches(sub));
        Assert.Empty(forward.Event.BlockedBy(sub));
    }

    [Fact]
    public void BackfillSample_DoesNotMatchVersionUp() {
        var backfill = FeedSamples.Find(FeedEventKinds.ProtoBuild, "backfill");
        Assert.NotNull(backfill);

        Assert.False(backfill.Event.Matches(
            Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ProtoGuards)));
    }

    [Fact]
    public void BarePeriodicalsSample_BlockedWhenAspectsRequired() {
        var bare = FeedSamples.Find(FeedEventKinds.PeriodicalsChanged, "bare");
        Assert.NotNull(bare);

        var sub = Probe(FeedEventKinds.PeriodicalsChanged, "any", FeedEventKinds.FilterRequireAspects);
        Assert.True(bare.Event.Matches(sub));
        Assert.Equal(FeedEventKinds.FilterRequireAspects, Assert.Single(bare.Event.BlockedBy(sub)));

        var identified = FeedSamples.Find(FeedEventKinds.PeriodicalsChanged, "identified");
        Assert.NotNull(identified);
        Assert.Empty(identified.Event.BlockedBy(sub));
    }

    [Fact]
    public void SampleBodies_AreNonEmptyJson() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in FeedSamples.For(kind.Key)) {
                string body = sample.Event.BuildBody(null);
                Assert.StartsWith("{", body, StringComparison.Ordinal);
                Assert.Contains("embeds", body, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("#notify", true, null, "config")]
    [InlineData("#notify_7", true, 7, "config")]
    [InlineData("#notify_7_preview", true, 7, "preview")]
    [InlineData("#notify_7_history", true, 7, "history")]
    [InlineData("#notify_7_bogus", true, 7, "config")]
    [InlineData("#notify_abc", true, null, "config")]
    [InlineData("#android_111358", false, null, "config")]
    [InlineData("", false, null, "config")]
    public void ParseHash_Grammar(string hash, bool match, int? id, string mode) {
        (bool gotMatch, int? gotId, string gotMode) = NotificationsWorkbenchState.ParseHash(hash);

        Assert.Equal(match, gotMatch);
        Assert.Equal(id, gotId);
        Assert.Equal(mode, gotMode);
    }

    [Fact]
    public void Hash_RoundTrips() {
        var state = new NotificationsWorkbenchState();
        Assert.Equal("notify", state.Hash());

        state.Creating = false;
        state.SelectedId = 12;
        Assert.Equal("notify_12", state.Hash());

        state.Mode = NotificationModes.History;
        Assert.Equal("notify_12_history", state.Hash());

        (bool match, int? id, string mode) = NotificationsWorkbenchState.ParseHash(state.Hash());
        Assert.True(match);
        Assert.Equal(12, id);
        Assert.Equal(NotificationModes.History, mode);
    }

    [Fact]
    public void Hash_HasNoLeadingHash_BecauseHashNavAddsIt() {
        var state = new NotificationsWorkbenchState {
            Creating = false,
            SelectedId = 7,
            Mode = NotificationModes.Preview
        };

        Assert.DoesNotContain("#", state.Hash(), StringComparison.Ordinal);
        Assert.Equal("notify_7_preview", state.Hash());
    }
}
