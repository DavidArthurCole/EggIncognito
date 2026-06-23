using Bunit;
using EggIncognito.Components.Pages;
using EggIncognito.Components.Protos;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// The redesigned /protos page: a toolbar projected into the nav, a real registry table, and an
// admin-only backfill panel below it. The admin gate is snapshotted from ICurrentUser during the
// request (the controller ACL is the real gate; this is courtesy UX). DB-free: with no HttpContext
// the self-calls fail and degrade to an empty table, which is what the empty-state assertions cover.
public class ProtosPageTests
{
    // Page-level: the prerendered /protos returns 200 with the registry shell. Anonymous, so no
    // backfill panel renders.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        [Fact]
        public async Task Protos_Anonymous_RendersTableShell_NoBackfillPanel()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/protos");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("Proto Registry", html);
            // The admin-only panel must not render for an anonymous caller.
            Assert.DoesNotContain("id=\"backfillPanel\"", html);
        }

        // Subscribe + Sources are modal overlays on /protos now; the legacy routes redirect there. The
        // redirect page must still respond 200 (a client-side NavigateTo), not 404.
        [Fact]
        public async Task SubscribeRoute_StillResponds() =>
            Assert.Equal(System.Net.HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/subscribe")).StatusCode);

        [Fact]
        public async Task SourcesRoute_StillResponds() =>
            Assert.Equal(System.Net.HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/sources")).StatusCode);
    }

    // Component-level (bUnit): a faked ICurrentUser drives the admin gate. Admin renders the backfill
    // panel; viewer does not. The empty registry table renders its empty state (no DB).
    public class Component : BunitContext
    {
        private void Wire(UserRole role)
        {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            // Copy/navigate JS only fires on click; tolerate any unplanned invocation during render.
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public void Admin_ShowsBackfillPanel()
        {
            Wire(UserRole.Admin);
            var cut = Render<Protos>();
            Assert.NotNull(cut.Find("#backfillPanel"));
            Assert.Contains("Missing versions", cut.Markup);
        }

        [Fact]
        public void Viewer_HidesBackfillPanel()
        {
            Wire(UserRole.Viewer);
            var cut = Render<Protos>();
            Assert.Empty(cut.FindAll("#backfillPanel"));
        }

        [Fact]
        public void EmptyRegistry_ShowsEmptyState()
        {
            Wire(UserRole.Viewer);
            var cut = Render<Protos>();
            // No DB -> the load fails and degrades to the styled empty table.
            Assert.Contains("No proto versions yet.", cut.Markup);
        }

        [Fact]
        public void SourcesPanel_RendersAttribution()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
            var cut = Render<EggIncognito.Components.Protos.ProtoSourcesPanel>();
            Assert.Contains("elgranjero", cut.Markup);
            Assert.Contains("Device farm", cut.Markup);
        }

        [Fact]
        public void BackfillPanel_RendersActiveSourceRows()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
            var cut = Render<BackfillPanel>();
            // GitHub + Android only; the dead Apple iTunes/Archive sources were removed (gave nothing).
            Assert.Contains("GitHub", cut.Markup);
            Assert.Contains("Android", cut.Markup);
            Assert.DoesNotContain("Apple", cut.Markup);
        }

        [Fact]
        public void MissingVersionsPanel_RendersContributeNote_AndEmptyState()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
            var cut = Render<MissingVersionsPanel>();
            // No DB -> known list empty state + the public contribute ask is shown.
            Assert.Contains("No discovered versions", cut.Markup);
            Assert.Contains("send the file my way", cut.Markup);
        }
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => role != UserRole.Viewer;
        public string? DiscordId => IsAuthenticated ? "fake" : null;
        public string? Username => IsAuthenticated ? "fake" : null;
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}
