using EggIncognito.Services.Api;

namespace EggIncognito.Tests;

public class ApiWorkbenchDatasetHashTests {
    [Fact]
    public void ApplyHash_ReadsARootDataset() {
        var state = new ApiWorkbenchState();
        Assert.True(state.ApplyHash("#data/periodical/get_periodicals"));
        Assert.Equal("periodical", state.Group);
        Assert.Equal("get_periodicals", state.Id);
        Assert.Null(state.Sub);
    }

    [Fact]
    public void ApplyHash_ReadsAChildDataset() {
        var state = new ApiWorkbenchState();
        Assert.True(state.ApplyHash("#data/periodical/config/shells"));
        Assert.Equal("periodical", state.Group);
        Assert.Equal("config", state.Id);
        Assert.Equal("shells", state.Sub);
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
    public void ApplyHash_RejectsEverythingThatIsNotADataGrammar(string hash) {
        Assert.False(new ApiWorkbenchState().ApplyHash(hash));
    }

    [Theory]
    [InlineData("#data/gamedata/mission.json")]
    [InlineData("#data/gamedata/mission.about")]
    [InlineData("#data/gamedata/mis.sion.about")]
    [InlineData("#data/game.data/mission")]
    [InlineData("#data/periodical/afx-config/eiafx.about")]
    public void ApplyHash_RejectsASegmentCarryingADot(string hash) {
        Assert.False(new ApiWorkbenchState().ApplyHash(hash));
    }

    [Fact]
    public void Hash_IsApiWithoutASelection() {
        Assert.Equal("api", new ApiWorkbenchState().Hash());
    }

    [Fact]
    public void Hash_RoundTripsARoot() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.Equal("api/data/gamedata/mission", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("gamedata", fresh.Group);
        Assert.Equal("mission", fresh.Id);
        Assert.Null(fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsAChild() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("periodical", "config", "shells");

        Assert.Equal("api/data/periodical/config/shells", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("periodical", fresh.Group);
        Assert.Equal("config", fresh.Id);
        Assert.Equal("shells", fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsTheCapturePane() {
        var state = new ApiWorkbenchState { Kind = ApiSelectionKind.Capture };

        Assert.Equal("api/capture", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(ApiSelectionKind.Capture, fresh.Kind);
    }

    [Fact]
    public void ApplyHash_LeavesTheStateAloneWhenItDoesNotMatch() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.False(state.ApplyHash("#notify_7"));
        Assert.Equal("mission", state.Id);
    }

    [Fact]
    public void TheApiWorkbenchHasNoModes() {
        var state = new ApiWorkbenchState();

        Assert.Empty(state.Modes);
        Assert.Equal("", state.DefaultMode);
    }
}
