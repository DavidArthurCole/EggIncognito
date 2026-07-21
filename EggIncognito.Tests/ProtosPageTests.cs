using Bunit;
using EggIncognito.Components.Pages;
using EggIncognito.Components.Protos;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class ProtosPageTests
{
   
   
    [Collection(SharedAppCollection.Name)]
    public class Integration
    {
        private readonly WebApplicationFactory<Program> _f;
        public Integration(SharedAppFactory f) => _f = f;

        [Fact]
        public async Task Protos_Anonymous_RendersTableShell_NoBackfillPanel()
        {
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

   
   
    public class Component : BunitContext
    {
        private void Wire(UserRole role)
        {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
           
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public void Admin_ShowsBackfillControls_InSourcesWidget()
        {
            Wire(UserRole.Admin);
            var cut = Render<Protos>();
           
            Assert.Contains("Re-import from elgranjero", cut.Markup);
            Assert.Contains("Missing versions", cut.Markup);
        }

        [Fact]
        public void Viewer_HidesBackfillControls()
        {
            Wire(UserRole.Viewer);
            var cut = Render<Protos>();
            Assert.DoesNotContain("Re-import from elgranjero", cut.Markup);
        }

        [Fact]
        public void EmptyRegistry_ShowsEmptyState()
        {
            Wire(UserRole.Viewer);
            var cut = Render<Protos>();
           
            Assert.Contains("No proto versions yet.", cut.Markup);
        }

        [Fact]
        public void SourcesPanel_RendersAttribution_TrimmedSources()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
            var cut = Render<EggIncognito.Components.Protos.ProtoSourcesPanel>();
           
            Assert.Contains("elgranjero", cut.Markup);
            Assert.Contains("Device farm", cut.Markup);
            Assert.Contains("Fandom", cut.Markup);
            Assert.DoesNotContain("APKPure", cut.Markup);
            Assert.DoesNotContain("Uptodown", cut.Markup);
            Assert.DoesNotContain("iTunes", cut.Markup);
        }

        [Fact]
        public void SourcesPanel_Admin_ShowsRefreshIcons_ViewerDoesNot()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
           
            var admin = Render<EggIncognito.Components.Protos.ProtoSourcesPanel>(p => p.Add(x => x.IsAdmin, true));
            Assert.Contains("Re-import from elgranjero", admin.Markup);
            Assert.Contains("Re-import from Fandom", admin.Markup);
            Assert.Contains("Refresh connected devices now", admin.Markup);
            var viewer = Render<EggIncognito.Components.Protos.ProtoSourcesPanel>(p => p.Add(x => x.IsAdmin, false));
            Assert.DoesNotContain("Re-import from elgranjero", viewer.Markup);
        }

        [Fact]
        public void MissingVersionsPanel_RendersContributeNote_AndEmptyState()
        {
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
            JSInterop.Mode = JSRuntimeMode.Loose;
            var cut = Render<MissingVersionsPanel>();
           
            Assert.Contains("No discovered versions", cut.Markup);
            Assert.Contains("Offer to registry", cut.Markup);
        }
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser
    {
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
