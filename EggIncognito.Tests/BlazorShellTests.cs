using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// The landing page (/) is the first tab ported to Blazor Server (Razor Components). This proves the
// Blazor shell renders: the server-gated nav, the GitHub bubble, the tailwind sheet link, and the
// blazor.web.js framework script. A regression in App.razor / MainLayout / TopNav / Home is caught here.
public class BlazorShellTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public BlazorShellTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Home_RendersBlazorShell()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("app-nav", html);
        Assert.Contains("gh-bubble", html);
        Assert.Contains("/tailwind.css", html);
        Assert.Contains("blazor.web.js", html);
    }
}
