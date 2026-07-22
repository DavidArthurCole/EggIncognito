using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ImportTabStyleTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task ImportPage_UsesTailwind_NotBespokeSheet() {
        var c = _f.CreateClient();
        var html = await c.GetStringAsync("/import");
        Assert.DoesNotContain("import/styles.css", html);
        Assert.Contains("/tailwind.css", html);
        Assert.Contains("class=\"panel\"", html);
    }

    [Fact]
    public async Task BespokeImportSheet_IsGone() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/import/styles.css");

        Assert.NotEqual(System.Net.HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task CompiledSheet_DefinesComponentClasses() {
        var c = _f.CreateClient();
        var css = await c.GetStringAsync("/tailwind.css");

        Assert.Contains(".panel", css);
        Assert.Contains(".dropzone", css);
        Assert.Contains(".btn-primary", css);
        Assert.Contains(".result-pre", css);
    }
}
