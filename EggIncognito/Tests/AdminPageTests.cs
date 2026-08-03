using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Pages;
using EggIncognito.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class AdminPageTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Admin_Anonymous_RendersDeniedState() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/admin");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string html = await r.Content.ReadAsStringAsync();
            Assert.Contains("adminMain", html);
            Assert.Contains("id=\"denied\"", html);

            Assert.Contains("Login unavailable", html);
            Assert.DoesNotContain("<h2>Users</h2>", html);
        }
    }


    public class Component : BunitContext {
        private void Wire(UserRole role) {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddSingleton(new AuthState(false));
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsDenied_NotPanel() {
            Wire(UserRole.Viewer);
            var cut = Render<Admin>();
            Assert.NotNull(cut.Find("#denied"));
            Assert.Empty(cut.FindAll("h2"));
        }

        [Fact]
        public void Admin_ShowsPanel_NotDenied() {
            Wire(UserRole.Admin);
            var cut = Render<Admin>();
            Assert.Empty(cut.FindAll("#denied"));
            Assert.Contains("Users", cut.Markup);
            Assert.NotNull(cut.Find("#log"));
        }
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => role != UserRole.Viewer;
        public Guid? UserId => null;
        public string? DiscordId => IsAuthenticated ? "fake" : null;
        public string? Username => IsAuthenticated ? "fake" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => role >= need;
    }
}
