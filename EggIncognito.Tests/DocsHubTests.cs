using Bunit;
using EggIncognito.Components.Shared;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// The registry is DB-free (proto reflection + curated lists), so the hub renders with no Postgres.
public class DocsHubTests
{
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        // Legacy /docs route now redirects into the Inspector; must still respond 200, not 404.
        [Fact]
        public async Task DocsRoute_StillResponds()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/docs");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        }

        [Fact]
        public async Task SubjectTags_ConfigKind_Accepted()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/api/docs/subject-tags/config/AppMode");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            Assert.Equal("[]", body.Trim());
        }
    }

    public class MarkdownComponent : BunitContext
    {
        [Fact]
        public void Markdown_RendersBold()
        {
            var cut = Render<Markdown>(p => p.Add(c => c.Body, "**hi**"));
            Assert.Contains("<strong>hi</strong>", cut.Markup);
        }
    }

    public class DocHelpComponent : BunitContext
    {
        private void Wire()
        {
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            Services.AddSingleton<IRouteCatalog>(new RouteCatalog("__no_routes_yaml__"));
            Services.AddSingleton<IDocRegistry, DocRegistry>();
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
        }

        [Fact]
        public void DocHelp_KnownConfigKey_RendersAffordance()
        {
            Wire();
            var cut = Render<DocHelp>(p => p
                .Add(c => c.Kind, "config")
                .Add(c => c.Key, "AppMode"));
            Assert.NotNull(cut.Find(".dochelp"));
        }

        [Fact]
        public void DocHelp_UnknownSubject_RendersNothing()
        {
            Wire();
            var cut = Render<DocHelp>(p => p
                .Add(c => c.Kind, "config")
                .Add(c => c.Key, "NoSuchKey"));
            Assert.Empty(cut.Markup.Trim());
        }
    }
}
