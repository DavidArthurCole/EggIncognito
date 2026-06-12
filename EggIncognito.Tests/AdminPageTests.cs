using Bunit;
using EggIncognito.Components.Pages;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// The Admin tab is the first interactive Blazor tab (role selects + delete buttons need handlers). The
// denied/panel split is gated server-side by ICurrentUser (courtesy UX; the controller ACL is the real
// gate). These confirm the page renders and an anonymous caller sees the denied state, not the panel.
public class AdminPageTests
{
    // Page-level: the prerendered /admin returns 200 and renders the denied login link for an anonymous
    // (non-admin) caller. Mirrors LayoutRenderTests / BlazorShellTests.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(WebApplicationFactory<Program> f) =>
            _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

        [Fact]
        public async Task Admin_Anonymous_RendersDeniedState()
        {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/admin");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("adminMain", html);
            Assert.Contains("id=\"denied\"", html);
            Assert.Contains("/login?returnUrl=/admin", html);
            // The panel's Users heading must not render for a non-admin.
            Assert.DoesNotContain("<h2>Users</h2>", html);
        }
    }

    // Component-level (bUnit): a faked ICurrentUser drives the gate. Anonymous shows denied; admin shows
    // the panel. The HttpContextAccessor has no HttpContext in the test, so the panel's list loads no-op
    // to empty tables (the graceful no-DB path), which is exactly what we assert degrades cleanly.
    public class Component : BunitContext
    {
        private void Wire(UserRole role)
        {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsDenied_NotPanel()
        {
            Wire(UserRole.Viewer);
            var cut = Render<Admin>();
            Assert.NotNull(cut.Find("#denied"));
            Assert.Empty(cut.FindAll("h2"));
        }

        [Fact]
        public void Admin_ShowsPanel_NotDenied()
        {
            Wire(UserRole.Admin);
            var cut = Render<Admin>();
            Assert.Empty(cut.FindAll("#denied"));
            Assert.Contains("Users", cut.Markup);
            Assert.NotNull(cut.Find("#log"));
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
