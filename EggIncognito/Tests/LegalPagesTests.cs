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
        string html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Terms of Service", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task PrivacyPage_Renders_WithDisclaimer() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/privacy");
        res.EnsureSuccessStatusCode();
        string html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Privacy", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task AboutPage_HoldsDisclaimer_AndLegalLinks() {
        using var client = _factory.CreateClient();
        string html = await client.GetStringAsync("/about");
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains(Disclaimer, html);
    }

    [Fact]
    public async Task LayoutFooter_RendersOncePerPage() {
        using var client = _factory.CreateClient();
        foreach (string path in new[] { "/", "/terms", "/support" }) {
            string html = await client.GetStringAsync(path);
            Assert.Equal(1, html.Split("id=\"siteFooter\"").Length - 1);
        }
    }
}
