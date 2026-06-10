using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Guards the Phase 2 Import-tab migration: the page must serve, must NOT link the deleted bespoke
// import/styles.css, must link the compiled Tailwind sheet, and that sheet must define the shared
// component classes the migrated markup depends on. A regression in the shim or an accidental
// re-introduction of the old sheet is caught here.
public class ImportTabStyleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public ImportTabStyleTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task ImportPage_UsesTailwind_NotBespokeSheet()
    {
        var c = _f.CreateClient();
        var html = await c.GetStringAsync("/import");
        Assert.DoesNotContain("import/styles.css", html);
        Assert.Contains("/tailwind.css", html);
        Assert.Contains("class=\"panel\"", html); // migrated to the component class
    }

    [Fact]
    public async Task BespokeImportSheet_IsGone()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/import/styles.css");
        // The file is deleted, so static-file middleware does not serve it. A missing path then falls
        // through to SimulationController's POST/OPTIONS catch-all, which rejects the GET with 405 (not
        // 404). Either way it is NOT served (200) - that is what "gone" means here.
        Assert.NotEqual(System.Net.HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task CompiledSheet_DefinesComponentClasses()
    {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");
        // The component layer the migrated markup depends on.
        Assert.Contains(".panel", css);
        Assert.Contains(".dropzone", css);
        Assert.Contains(".btn-primary", css);
        Assert.Contains(".result-pre", css);
    }
}
