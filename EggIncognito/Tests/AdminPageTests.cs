using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Pages;
using EggIncognito.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Notifications;
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
        public async Task Admin_Anonymous_ServesTheShell() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/admin");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("blazor.web.js", await r.Content.ReadAsStringAsync());
        }
    }


    public class Component : BunitContext {
        private void Wire(UserRole role) {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddSingleton(new AuthState(false));
            Services.AddScoped<DeviceWorkbenchState>();
            Services.AddScoped<NotificationsWorkbenchState>();
            Services.AddScoped<AdminWorkbenchState>();
            Services.AddSingleton<AdminNotifier>();
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsDenied_NotPanel() {
            Wire(UserRole.Viewer);
            var cut = Render<Admin>();
            Assert.NotNull(cut.Find("#adminMain"));
            Assert.NotNull(cut.Find("#denied"));
            Assert.Contains("Login is not configured.", cut.Markup);
            Assert.Empty(cut.FindAll("h2"));
        }

        [Fact]
        public void Admin_ShowsPanel_NotDenied() {
            Wire(UserRole.Admin);
            var cut = Render<Admin>();
            Assert.Empty(cut.FindAll("#denied"));
            Assert.Contains("Users", cut.Markup);
            Assert.NotNull(cut.Find(".admin-wb-rail"));
            Assert.Equal(4, cut.FindAll(".admin-wb-group").Count);
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
