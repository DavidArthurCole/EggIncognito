using EggIncognito.Services.Workbench;

namespace EggIncognito.Tests;

public class WorkbenchStateBaseTests {
    [Fact]
    public void Mode_DefaultsToFirstMode() {
        var state = new TwoModeState();
        Assert.Equal("alpha", state.DefaultMode);
        Assert.Equal("alpha", state.Mode);
    }

    [Fact]
    public void Mode_RoundTripsAKnownKey() {
        var state = new TwoModeState { Mode = "beta" };
        Assert.Equal("beta", state.Mode);
    }

    [Theory]
    [InlineData("gamma")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ALPHA")]
    public void Mode_NormalizesUnknownBackToDefault(string? value) {
        var state = new TwoModeState { Mode = "beta" };
        state.Mode = value!;
        Assert.Equal("alpha", state.Mode);
    }

    [Fact]
    public void Mode_IsEmptyWhenAWorkbenchHasNoModes() {
        var state = new NoModeState();
        Assert.Equal("", state.DefaultMode);
        Assert.Equal("", state.Mode);
        state.Mode = "anything";
        Assert.Equal("", state.Mode);
    }

    [Fact]
    public void HashSurface_IsOffByDefault() {
        var state = new TwoModeState();
        Assert.Equal("", state.HashPrefix);
        Assert.Null(state.Hash());
        Assert.False(state.ApplyHash("#anything"));
        Assert.False(state.OwnsHash("#anything"));
    }

    [Theory]
    [InlineData("notify", true)]
    [InlineData("#notify", true)]
    [InlineData("notify_12", true)]
    [InlineData("#notify_12_preview", true)]
    [InlineData("notifyx", false)]
    [InlineData("ios_1140823", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OwnsHash_MatchesOnlyItsOwnPrefix(string? hash, bool owned) {
        Assert.Equal(owned, new PrefixedState().OwnsHash(hash));
    }

    private sealed class TwoModeState : WorkbenchStateBase {
        public override IReadOnlyList<WorkbenchMode> Modes { get; } = [
            new WorkbenchMode("alpha", "Alpha"),
            new WorkbenchMode("beta", "Beta")
        ];
    }

    private sealed class NoModeState : WorkbenchStateBase {
        public override IReadOnlyList<WorkbenchMode> Modes { get; } = [];
    }

    private sealed class PrefixedState : WorkbenchStateBase {
        public override IReadOnlyList<WorkbenchMode> Modes { get; } = [];

        public override string HashPrefix => "notify";
    }
}
