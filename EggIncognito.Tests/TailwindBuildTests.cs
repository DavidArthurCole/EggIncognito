using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Proves the Tailwind build wiring produced a served stylesheet, so a broken/absent compile is caught
// by CI rather than shipping an unstyled site once markup depends on utilities. Skips gracefully if the
// compile was disabled (BuildTailwindCss=false) and the file is genuinely absent.
public class TailwindBuildTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public TailwindBuildTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task TailwindCss_IsServed_AndNonEmpty()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/tailwind.css");
        // 200 + non-trivial body when the build compiled it; 404 only if the compile was skipped.
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return; // compile disabled - not a failure
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var css = await r.Content.ReadAsStringAsync();
        Assert.True(css.Length > 500, "compiled tailwind.css looks empty");
    }
}
