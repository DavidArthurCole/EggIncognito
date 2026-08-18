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
    public void BareConfigSample_BlockedWhenAspectsRequired() {
        var bare = FeedSamples.Find(FeedEventKinds.ConfigChanged, "bare");
        Assert.NotNull(bare);

        var sub = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed,
            FeedEventKinds.FilterRequireAspects);
        Assert.True(bare.Event.Matches(sub));
        Assert.Equal(FeedEventKinds.FilterRequireAspects, Assert.Single(bare.Event.BlockedBy(sub)));

        var identified = FeedSamples.Find(FeedEventKinds.ConfigChanged, "periodicals");
        Assert.NotNull(identified);
        Assert.Empty(identified.Event.BlockedBy(sub));
    }

    [Fact]
    public void AfxSample_HasAspects_ButNoIdentifiers() {
        var afx = FeedSamples.Find(FeedEventKinds.ConfigChanged, "afx");
        Assert.NotNull(afx);

        var aspects = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed,
            FeedEventKinds.FilterRequireAspects);
        Assert.Empty(afx.Event.BlockedBy(aspects));

        var ids = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed,
            FeedEventKinds.FilterRequireIds);
        Assert.Equal(FeedEventKinds.FilterRequireIds, Assert.Single(afx.Event.BlockedBy(ids)));
    }

    [Fact]
    public void GameDataSamples_OnlyBinaryUpMatchesTheBinaryUpTrigger() {
        var moved = FeedSamples.Find(FeedEventKinds.GameDataRebuilt, "binary_up");
        var same = FeedSamples.Find(FeedEventKinds.GameDataRebuilt, "same_binary");
        Assert.NotNull(moved);
        Assert.NotNull(same);

        var sub = Probe(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp);
        Assert.True(moved.Event.Matches(sub));
        Assert.False(same.Event.Matches(sub));

        var any = Probe(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild);
        Assert.True(moved.Event.Matches(any));
        Assert.True(same.Event.Matches(any));
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
    [InlineData("#notify", true, null)]
    [InlineData("#notify_7", true, 7)]
    [InlineData("#notify_abc", true, null)]
    [InlineData("#android_111358", false, null)]
    [InlineData("#data/periodical/get_periodicals", false, null)]
    [InlineData("", false, null)]
    public void ParseHash_Grammar(string hash, bool match, int? id) {
        (bool gotMatch, int? gotId) = NotificationsWorkbenchState.ParseHash(hash);

        Assert.Equal(match, gotMatch);
        Assert.Equal(id, gotId);
    }

    [Theory]
    [InlineData("#notify_7_preview")]
    [InlineData("#notify_7_history")]
    [InlineData("#notify_7_bogus")]
    [InlineData("#notify_7_history_extra")]
    public void ParseHash_IgnoresTheLegacyModeSegment(string hash) {
        (bool match, int? id) = NotificationsWorkbenchState.ParseHash(hash);

        Assert.True(match);
        Assert.Equal(7, id);
    }

    [Fact]
    public void Hash_RoundTrips() {
        var state = new NotificationsWorkbenchState();
        Assert.Equal("notify", state.Hash());

        state.Creating = false;
        state.SelectedId = 12;
        Assert.Equal("notify_12", state.Hash());

        (bool match, int? id) = NotificationsWorkbenchState.ParseHash(state.Hash());
        Assert.True(match);
        Assert.Equal(12, id);
    }

    [Fact]
    public void Hash_NeverEmitsAThirdSegment() {
        var state = new NotificationsWorkbenchState { Creating = false, SelectedId = 7 };

        Assert.Equal("notify_7", state.Hash());
        Assert.DoesNotContain("#", state.Hash()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyHash_RestoresTheSelectionFromALegacyLink() {
        var state = new NotificationsWorkbenchState();

        Assert.True(state.ApplyHash("#notify_7_history"));
        Assert.False(state.Creating);
        Assert.Equal(7, state.SelectedId);

        Assert.True(state.ApplyHash("#notify"));
        Assert.True(state.Creating);
        Assert.Null(state.SelectedId);

        Assert.False(state.ApplyHash("#android_111358"));
    }

    [Fact]
    public void TheWorkbenchHasNoModes() {
        var state = new NotificationsWorkbenchState();

        Assert.Empty(state.Modes);
        Assert.Equal("", state.DefaultMode);
        Assert.Equal("", state.Mode);
        Assert.True(state.OwnsHash("#notify_7"));
        Assert.False(state.OwnsHash("#data/periodical/get_periodicals"));
    }
}
