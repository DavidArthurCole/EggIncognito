using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class SupportPageTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task SupportPage_Renders() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/support");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("GitHub Sponsors", html);
        Assert.Contains("buymeacoffee.com/davidarthurcole", html);
        Assert.Contains("patreon.com/c/DavidArthurCole", html);
    }

    [Fact]
    public async Task SupportPage_HidesConnectSection_WithoutOAuth() {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/support");
        Assert.DoesNotContain("Connect your account", html);
    }
}
