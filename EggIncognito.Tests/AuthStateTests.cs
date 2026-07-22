using EggIncognito.Services;

namespace EggIncognito.Tests;

public class AuthStateTests {
    [Fact]
    public void WidgetEnabled_False_WhenNoIdentityHostUrl() {
        var state = new AuthState(IdentityApiEnabled: true);
        Assert.False(state.WidgetEnabled);
        Assert.False(state.Enabled);
    }

    [Fact]
    public void WidgetEnabled_False_WhenIdentityApiOff() {
        var state = new AuthState(IdentityApiEnabled: false, IdentityHostUrl: "http://identity.local");
        Assert.False(state.WidgetEnabled);
    }

    [Fact]
    public void WidgetEnabled_True_WhenApiAndHostUrlPresent() {
        var state = new AuthState(IdentityApiEnabled: true, IdentityHostUrl: "http://identity.local");
        Assert.True(state.WidgetEnabled);
        Assert.True(state.Enabled);
    }
}
