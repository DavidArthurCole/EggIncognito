using Bunit;
using EggIncognito.Components.Shared;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// Phase 3C: the docs hub page + shared Markdown components. The registry is DB-free (proto reflection +
// curated lists), so the hub renders with no Postgres. ValidKind was widened for the new subject kinds.
public class DocsHubTests
{
    // Page-level: the prerendered /docs returns 200 and renders the hub shell with the 4 group titles.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        // Docs folded into the Inspector; the legacy /docs route now redirects there. It must still
        // respond 200 (client-side NavigateTo), not 404. The proto doc API + DocHelp affordance (asserted
        // below) carry the actual docs surface now.
        [Fact]
        public async Task DocsRoute_StillResponds()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/docs");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        }

        // The widened ValidKind now accepts "config". With no DB the controller returns 200 + [], not 400.
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

    // Component-level (bUnit): the Markdown render component wires MarkdownRenderer (markdown -> safe HTML).
    public class MarkdownComponent : BunitContext
    {
        [Fact]
        public void Markdown_RendersBold()
        {
            var cut = Render<Markdown>(p => p.Add(c => c.Body, "**hi**"));
            Assert.Contains("<strong>hi</strong>", cut.Markup);
        }
    }

    // Component-level: DocHelp renders the registry summary for a known config key, and nothing for an
    // unknown subject.
    public class DocHelpComponent : BunitContext
    {
        private void Wire()
        {
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            // Config subjects (what these tests assert on) are curated and need no routes, so an empty
            // catalog (non-existent yaml path) is enough. proto reflection drives the message subtree.
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
