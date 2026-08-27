using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class BlazorShellTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task Root_ServesTheProtosSurfaceInTheBlazorShell() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("pd-grid", html);
        Assert.Contains("pd-brand", html);
        Assert.Contains("pd-support", html);
        Assert.Contains("/styles.css", html);
        Assert.Contains("blazor.web.js", html);
    }

    [Fact]
    public async Task AboutWidget_CarriesTheIconRow_AndTheFloatingBubblesAreGone() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/");
        Assert.Contains("pd-brand-links", html);
        Assert.Contains("aria-label=\"EggIncognito on GitHub\"", html);
        Assert.Contains("aria-label=\"Support the project\"", html);
        Assert.DoesNotContain("gh-bubble", html);
        Assert.DoesNotContain("support-bubble", html);
    }

    [Fact]
    public async Task Shell_HasNoTopBar_AndCarriesTheLegalFooter() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/");
        Assert.DoesNotContain("app-nav", html);
        Assert.Contains("id=\"siteFooter\"", html);
        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains("LICENSE", html);
    }
}
