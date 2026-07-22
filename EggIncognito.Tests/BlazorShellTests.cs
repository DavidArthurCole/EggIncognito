using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class BlazorShellTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task Home_RendersBlazorShell() {
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
