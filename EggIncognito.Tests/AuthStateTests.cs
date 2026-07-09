using EggIncognito.Services;

namespace EggIncognito.Tests;

public class AuthStateTests
{
    [Fact]
    public void WidgetEnabled_False_WhenNoIdentityHostUrl()
    {
        var state = new AuthState(DiscordEnabled: true, AuthentikEnabled: false);
        Assert.False(state.WidgetEnabled);
    }

    [Fact]
    public void WidgetEnabled_False_WhenNoProviderWired()
    {
        var state = new AuthState(DiscordEnabled: false, AuthentikEnabled: false, IdentityHostUrl: "http://identity.local");
        Assert.False(state.WidgetEnabled);
    }

    [Fact]
    public void WidgetEnabled_True_WhenProviderAndHostUrlPresent()
    {
        var state = new AuthState(DiscordEnabled: true, AuthentikEnabled: false, IdentityHostUrl: "http://identity.local");
        Assert.True(state.WidgetEnabled);
    }
}
