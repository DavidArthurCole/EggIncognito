using EggIncognito.Services.Data;

namespace EggIncognito.Tests;

public class DataWorkbenchTests {
    [Fact]
    public void ParseHash_ReadsARootDataset() {
        (bool match, string group, string id, string? sub) =
            DataWorkbenchState.ParseHash("#data/periodical/get_periodicals");

        Assert.True(match);
        Assert.Equal("periodical", group);
        Assert.Equal("get_periodicals", id);
        Assert.Null(sub);
    }

    [Fact]
    public void ParseHash_ReadsAChildDataset() {
        (bool match, string group, string id, string? sub) =
            DataWorkbenchState.ParseHash("#data/periodical/config/shells");

        Assert.True(match);
        Assert.Equal("periodical", group);
        Assert.Equal("config", id);
        Assert.Equal("shells", sub);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#notify")]
    [InlineData("#notify_7_history")]
    [InlineData("#android_111358")]
    [InlineData("#ios_1140823...android_111358/split")]
    [InlineData("#data")]
    [InlineData("#data/")]
    [InlineData("#data/periodical")]
    [InlineData("#data/periodical/config/shells/extra")]
    [InlineData("#data//get_periodicals")]
    public void ParseHash_RejectsEverythingThatIsNotADataGrammar(string hash) {
        (bool match, _, _, _) = DataWorkbenchState.ParseHash(hash);

        Assert.False(match);
    }

    [Theory]
    [InlineData("#data/gamedata/mission.json")]
    [InlineData("#data/gamedata/mission.about")]
    [InlineData("#data/gamedata/mis.sion.about")]
    [InlineData("#data/game.data/mission")]
    [InlineData("#data/periodical/afx-config/eiafx.about")]
    public void ParseHash_RejectsASegmentCarryingADot(string hash) {
        (bool match, _, _, _) = DataWorkbenchState.ParseHash(hash);

        Assert.False(match);
    }

    [Fact]
    public void Hash_RoundTripsARoot() {
        var state = new DataWorkbenchState();
        state.Select("gamedata", "mission", null);

        Assert.Equal("data/gamedata/mission", state.Hash());

        var fresh = new DataWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("gamedata", fresh.Group);
        Assert.Equal("mission", fresh.Id);
        Assert.Null(fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsAChild() {
        var state = new DataWorkbenchState();
        state.Select("periodical", "config", "shells");

        Assert.Equal("data/periodical/config/shells", state.Hash());

        var fresh = new DataWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("periodical", fresh.Group);
        Assert.Equal("config", fresh.Id);
        Assert.Equal("shells", fresh.Sub);
    }

    [Fact]
    public void Hash_IsNullWithoutASelection() {
        Assert.Null(new DataWorkbenchState().Hash());
    }

    [Fact]
    public void ApplyHash_LeavesTheStateAloneWhenItDoesNotMatch() {
        var state = new DataWorkbenchState();
        state.Select("gamedata", "mission", null);

        Assert.False(state.ApplyHash("#notify_7"));
        Assert.Equal("mission", state.Id);
    }

    [Fact]
    public void TheWorkbenchHasNoModes() {
        var state = new DataWorkbenchState();

        Assert.Empty(state.Modes);
        Assert.Equal("", state.DefaultMode);
    }
}
