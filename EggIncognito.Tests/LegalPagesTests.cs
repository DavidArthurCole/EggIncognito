using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;


[Collection(SharedAppCollection.Name)]
public class LegalPagesTests(SharedAppFactory f) {
    private const string Disclaimer =
        "EggIncognito is an independent, fan-made tool and is not affiliated with, endorsed by, or";

    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task TermsPage_Renders_WithDisclaimer() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/terms");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Terms of Service", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task PrivacyPage_Renders_WithDisclaimer() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/privacy");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Privacy", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task AboutPage_HoldsDisclaimer_AndLegalLinks() {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/about");
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task Home_DoesNotPlasterTheFooter() {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.DoesNotContain("site-footer", html);
    }
}
