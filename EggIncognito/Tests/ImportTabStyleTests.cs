using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ImportTabStyleTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task ImportPage_UsesSharedSheet_NotBespokeSheet() {
        var c = _f.CreateClient();
        string html = await c.GetStringAsync("/import");
        Assert.DoesNotContain("import/styles.css", html);
        Assert.Contains("/styles.css", html);
        Assert.Contains("class=\"panel\"", html);
    }

    [Fact]
    public async Task BespokeImportSheet_IsGone() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/import/styles.css");

        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task CompiledSheet_DefinesComponentClasses() {
        var c = _f.CreateClient();
        string css = await c.GetStringAsync("/styles.css");

        Assert.Contains(".panel", css);
        Assert.Contains(".dropzone", css);
        Assert.Contains(".btn-primary", css);
        Assert.Contains(".result-pre", css);
    }
}
