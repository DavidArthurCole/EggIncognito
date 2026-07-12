using Bunit;
using EggIncognito.Components.Pages;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

// The denied/panel split is gated server-side by ICurrentUser (courtesy UX; the controller ACL is the real gate).
public class AdminPageTests
{
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
            // Neither provider is configured in this test host; LoginButton's fallback picks /login
            // when Authentik isn't wired (Auth.AuthentikEnabled false).
            Assert.Contains("/login?returnUrl=%2Fadmin", html);
            Assert.DoesNotContain("<h2>Users</h2>", html);
        }
    }

    // No HttpContext in the test, so the panel's list loads no-op to empty tables.
    public class Component : BunitContext
    {
        private void Wire(UserRole role)
        {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton(new AuthState(DiscordEnabled: false, AuthentikEnabled: false));
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
        public Guid? UserId => null;
        public string? DiscordId => IsAuthenticated ? "fake" : null;
        public string? Username => IsAuthenticated ? "fake" : null;
        public string? Avatar => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}
