using Bunit;
using EggIncognito.Components.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// The Import tab is a Blazor component (InputFile + drag-drop zone). It POSTs the chosen HAR to the
// CanWrite-gated /api/import/har; the component never ingests directly. These confirm the page renders
// its dropzone shell and that the Import button starts disabled until a file is chosen.
public class ImportPageTests
{
    // Page-level: the prerendered /import returns 200 with the dropzone chrome. Mirrors AdminPageTests.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        [Fact]
        public async Task Import_RendersDropzoneShell()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/import");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("Drop a HAR or .mitm file here", html);
            Assert.Contains("class=\"panel\"", html);
            Assert.Contains("dropzone", html);
            Assert.Contains("importBtn", html);
            Assert.Contains("importResult", html);
        }
    }

    // Component-level (bUnit): the Import button starts disabled (no file) and the dropzone renders.
    public class Component : BunitContext
    {
        [Fact]
        public void Import_StartsWithDisabledButton_AndDropzone()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            var cut = Render<Import>();
            Assert.NotNull(cut.Find("#dropZone"));
            Assert.True(cut.Find("#importBtn").HasAttribute("disabled"));
        }
    }
}
