using EggIncognito.Services.Data;

namespace EggIncognito.Tests;

public class DataWorkbenchTests {
    [Fact]
    public void ParseHash_ReadsARootDataset() {
        (bool match, string group, string id, string? sub, string mode) =
            DataWorkbenchState.ParseHash("#data/periodical/get_periodicals");

        Assert.True(match);
        Assert.Equal("periodical", group);
        Assert.Equal("get_periodicals", id);
        Assert.Null(sub);
        Assert.Equal(DataWorkbenchState.ModeData, mode);
    }

    [Fact]
    public void ParseHash_ReadsAChildDataset() {
        (bool match, string group, string id, string? sub, string mode) =
            DataWorkbenchState.ParseHash("#data/periodical/config/shells");

        Assert.True(match);
        Assert.Equal("periodical", group);
        Assert.Equal("config", id);
        Assert.Equal("shells", sub);
        Assert.Equal(DataWorkbenchState.ModeData, mode);
    }

    [Fact]
    public void ParseHash_ReadsTheModeSuffix() {
        (bool match, _, string id, _, string mode) = DataWorkbenchState.ParseHash("#data/gamedata/mission.about");

        Assert.True(match);
        Assert.Equal("mission", id);
        Assert.Equal(DataWorkbenchState.ModeAbout, mode);
    }

    [Fact]
    public void ParseHash_ReadsTheModeSuffixOnAChild() {
        (bool match, _, string id, string? sub, string mode) =
            DataWorkbenchState.ParseHash("#data/periodical/afx-config/eiafx.about");

        Assert.True(match);
        Assert.Equal("afx-config", id);
        Assert.Equal("eiafx", sub);
        Assert.Equal(DataWorkbenchState.ModeAbout, mode);
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
        (bool match, _, _, _, _) = DataWorkbenchState.ParseHash(hash);

        Assert.False(match);
    }

    [Theory]
    [InlineData("#data/gamedata/mission.json")]
    [InlineData("#data/gamedata/mis.sion.about")]
    [InlineData("#data/game.data/mission")]
    public void ParseHash_RejectsAnIdCarryingADot(string hash) {
        (bool match, _, _, _, _) = DataWorkbenchState.ParseHash(hash);

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
        Assert.Equal(DataWorkbenchState.ModeData, fresh.Mode);
    }

    [Fact]
    public void Hash_RoundTripsAChildWithAMode() {
        var state = new DataWorkbenchState();
        state.Select("periodical", "config", "shells");
        state.Mode = DataWorkbenchState.ModeAbout;

        Assert.Equal("data/periodical/config/shells.about", state.Hash());

        var fresh = new DataWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("shells", fresh.Sub);
        Assert.Equal(DataWorkbenchState.ModeAbout, fresh.Mode);
    }

    [Fact]
    public void Hash_OmitsTheDefaultMode() {
        var state = new DataWorkbenchState();
        state.Select("asset", "icon", null);
        state.Mode = DataWorkbenchState.ModeData;

        Assert.Equal("data/asset/icon", state.Hash());
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
    public void Modes_AreDataThenAbout() {
        var state = new DataWorkbenchState();

        Assert.Equal(new[] { DataWorkbenchState.ModeData, DataWorkbenchState.ModeAbout },
            state.Modes.Select(m => m.Key));
        Assert.Equal(DataWorkbenchState.ModeData, state.DefaultMode);
    }

    [Fact]
    public void UnknownMode_IsNotAcceptedAsAModeSuffix() {
        (bool match, _, _, _, _) = DataWorkbenchState.ParseHash("#data/gamedata/mission.history");

        Assert.False(match);
    }
}
