using Bunit;
using EggIncognito.Components.Pages;
using EggIncognito.Components.Protos;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Contract;

namespace EggIncognito.Tests;

public class ProtosPageTests {


    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Protos_Anonymous_RendersTableShell_NoBackfillPanel() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/protos");
            Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.Contains("Protos &amp; Data", html);

            Assert.DoesNotContain("id=\"backfillPanel\"", html);
        }



        [Fact]
        public async Task SubscribeRoute_StillResponds() =>
            Assert.Equal(System.Net.HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/subscribe")).StatusCode);

        [Fact]
        public async Task SourcesRoute_StillResponds() =>
            Assert.Equal(System.Net.HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/sources")).StatusCode);
    }



    public class Component : BunitContext {
        private void Wire(UserRole role) {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();

            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public void EmptyRegistry_ShowsEmptyState() {
            Wire(UserRole.Viewer);
            var cut = Render<Protos>();

            Assert.Contains("No proto versions yet.", cut.Markup);
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
