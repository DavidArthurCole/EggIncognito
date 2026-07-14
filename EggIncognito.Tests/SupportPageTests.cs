using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// The /support marketing page renders in all modes: perks, the three platform cards, and the FAQ.
// The connect-account section is auth-gated in markup and absent without OAuth config.
[Collection(SharedAppCollection.Name)]
public class SupportPageTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public SupportPageTests(SharedAppFactory f) => _factory = f;

    [Fact]
    public async Task SupportPage_Renders()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/support");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("GitHub Sponsors", html);
        Assert.Contains("buymeacoffee.com/davidarthurcole", html);
        Assert.Contains("patreon.com/c/DavidArthurCole", html);
    }

    [Fact]
    public async Task SupportPage_HidesConnectSection_WithoutOAuth()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/support");
        Assert.DoesNotContain("Connect your account", html);
    }
}
