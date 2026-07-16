using Bunit;
using EggIncognito.Components.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;


public class ImportPageTests
{
   
    [Collection(SharedAppCollection.Name)]
    public class Integration
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(SharedAppFactory f) => _f = f;

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
