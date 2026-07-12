using Bunit;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CapturePage = EggIncognito.Components.Pages.Capture;

namespace EggIncognito.Tests;

// Hosted capture surface: the mode payload capability, the /capture page branches per role, and the
// controller gates (live supporter re-check on start, supporter-or-contributor on hosted save).
public class HostedCapturePageTests
{
    private sealed class FakeAppMode(bool canCapture, bool hostedEnabled) : IAppMode
    {
        public AppMode Mode => AppMode.Hosted;
        public bool CanCapture => canCapture;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => hostedEnabled;
    }

    private sealed class FakeUser(bool authed, bool supporter, UserRole role = UserRole.Viewer) : ICurrentUser
    {
        public bool IsAuthenticated => authed;
        public Guid? UserId => authed ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        public string? DiscordId => authed ? "tester" : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }

    private sealed class FakeSupporters(bool result) : ISupporterStatus
    {
        public Task<bool> CheckAsync(string discordId, CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeRoutes : IRouteCatalog
    {
        public IReadOnlyList<RouteInfo> All() => [];
        public RouteInfo? Get(string path) => new(path, null, "PeriodicalsResponse", false, false, null, false, false);
    }

    private static CaptureSessionManager NewManager() =>
        new(HostedCaptureOptions.Defaults(),
            (key, basePort) => CaptureSessionManagerTests.NewSession(
                key == CaptureSessionManager.LocalKey ? 18080 : basePort));

    // Page-level over the real host: Hosted mode payload + the anonymous /capture branch.
    public class Integration : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _base;
        public Integration(WebApplicationFactory<Program> f) => _base = f;

        private WebApplicationFactory<Program> Hosted(bool enabled) =>
            _base.WithWebHostBuilder(b =>
            {
                b.UseSetting("NoBrowser", "true");
                b.UseSetting("AppMode", "Hosted");
                if (enabled)
                {
                    b.UseSetting("HostedCaptureEnabled", "true");
                    b.UseSetting("Capture:FrontDoorPort", "0"); // ephemeral; no fixed bind in tests
                    b.UseSetting("Capture:AddressSecret", "test-secret"); // required when hosted capture is on
                }
            });

        [Fact]
        public async Task Mode_Hosted_Default_ReportsHostedCaptureFalse()
        {
            var json = await Hosted(enabled: false).CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":false", json);
        }

        [Fact]
        public async Task Mode_Hosted_Enabled_ReportsHostedCaptureTrue()
        {
            var json = await Hosted(enabled: true).CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":true", json);
        }

        [Fact]
        public async Task CapturePage_HostedEnabled_Anonymous_ShowsLoginPrompt()
        {
            var c = Hosted(enabled: true).CreateClient();
            var html = await c.GetStringAsync("/capture");
            Assert.Contains("id=\"hostedLogin\"", html);
            // Neither provider is configured; LoginButton renders disabled "Login unavailable".
            Assert.Contains("Login unavailable", html);
            Assert.DoesNotContain("id=\"statsPanel\"", html);
        }

        [Fact]
        public async Task CaptureStart_HostedEnabled_Anonymous_Is401()
        {
            var c = Hosted(enabled: true).CreateClient();
            var r = await c.PostAsync("/api/capture/start", null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    // Component-level (bUnit): a faked ICurrentUser drives the page branch, AdminPageTests-style.
    public class Component : BunitContext
    {
        private void Wire(bool authed, bool supporter)
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<IAppMode>(new FakeAppMode(canCapture: false, hostedEnabled: true));
            Services.AddSingleton<ICurrentUser>(new FakeUser(authed, supporter));
            Services.AddSingleton(HostedCaptureOptions.Defaults());
            Services.AddSingleton(NewManager());
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Services.AddSingleton(new AuthState(DiscordEnabled: false, AuthentikEnabled: false));
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsLoginPrompt()
        {
            Wire(authed: false, supporter: false);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedLogin"));
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
        }

        [Fact]
        public void NonSupporter_ShowsPitchWithSupportLink()
        {
            Wire(authed: true, supporter: false);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedPitch"));
            Assert.Contains("href=\"/support\"", cut.Markup);
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
        }

        [Fact]
        public void Supporter_ShowsSetupCard_AndDashboard()
        {
            Wire(authed: true, supporter: true);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedSetupCard"));
            Assert.NotNull(cut.Find("#statsPanel"));
        }

        [Fact]
        public void Supporter_SetupCard_ShowsProxyAddress_NoAuthCredentials()
        {
            Wire(authed: true, supporter: true);
            var cut = Render<CapturePage>();
            var markup = cut.Markup;
            // The per-user IPv6 host placeholder + port + auth-off rows render.
            Assert.NotNull(cut.Find("#proxyHost"));
            Assert.Contains("Server", markup);
            Assert.Contains("Port", markup);
            Assert.Contains("Auth", markup);
            // No token / username credentials, no rotate button, no auto-proxy SSID card.
            Assert.Empty(cut.FindAll("#proxyProfileCard"));
            Assert.Empty(cut.FindAll("#mintedToken"));
            Assert.DoesNotContain("Username", markup);
            Assert.DoesNotContain("Token", markup);
            Assert.DoesNotContain("Cycle token", markup);
            Assert.DoesNotContain("SSID", markup);
            Assert.DoesNotContain("DM proxy profile", markup);
        }
    }

    // Controller gates, unit-level with fakes (StoredEndpointRoleTests style).
    public class StartGate
    {
        private static CaptureController Controller(
            CaptureSessionManager manager, ICurrentUser user, ISupporterStatus supporters) =>
            new(manager, new FakeAppMode(canCapture: false, hostedEnabled: true), user, supporters,
                HostedCaptureOptions.Defaults(), new EmptyServices());

        [Fact]
        public async Task Start_Anonymous_Is401()
        {
            var r = await Controller(NewManager(), new FakeUser(false, false), new FakeSupporters(true))
                .Start(CancellationToken.None);
            Assert.Equal(401, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Start_LiveCheckFails_Is403SupporterRequired()
        {
            // Even with the cookie claim set, the live re-check decides.
            var r = await Controller(NewManager(), new FakeUser(true, supporter: true), new FakeSupporters(false))
                .Start(CancellationToken.None);
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
            Assert.Contains("supporter_required", ((Microsoft.AspNetCore.Mvc.ObjectResult)r).Value!.ToString());
        }

        [Fact]
        public async Task Start_Supporter_StartsOwnSession()
        {
            var manager = NewManager();
            var r = await Controller(manager, new FakeUser(true, supporter: true), new FakeSupporters(true))
                .Start(CancellationToken.None);
            Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
            var session = manager.Get("tester");
            Assert.NotNull(session);
            Assert.Equal(CaptureState.Running, session!.State);
            await session.StopAsync();
        }

        [Fact]
        public async Task ProxyAddress_NonSupporter_Is403()
        {
            var r = await Controller(NewManager(), new FakeUser(true, supporter: false), new FakeSupporters(false))
                .ProxyAddress(CancellationToken.None);
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
        }
    }

    public class SaveGate
    {
        private static (CaptureController Controller, CaptureSession Session) WithFlowSession(ICurrentUser user)
        {
            var manager = NewManager();
            var session = manager.GetOrCreate("tester");
            var controller = new CaptureController(
                manager, new FakeAppMode(canCapture: false, hostedEnabled: true), user,
                new FakeSupporters(true), HostedCaptureOptions.Defaults(), new EmptyServices());
            return (controller, session);
        }

        private static long PublishFlow(CaptureSession session)
        {
            var stored = session.Hub.Publish(
                new DashboardFlow(0, "", "ei/first_contact", "POST", 200, null, null, "AA==", null),
                "12:00:00");
            return stored!.Id;
        }

        [Fact]
        public async Task Save_Hosted_ViewerNonSupporter_Is403()
        {
            var (controller, session) = WithFlowSession(new FakeUser(true, supporter: false, UserRole.Viewer));
            var id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Save_Hosted_Supporter_PassesGate_Then503NoDb()
        {
            var (controller, session) = WithFlowSession(new FakeUser(true, supporter: true, UserRole.Viewer));
            var id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode); // past the role gate, stopped by no-DB
        }

        [Fact]
        public async Task Save_Hosted_Contributor_PassesGate_Then503NoDb()
        {
            var (controller, session) = WithFlowSession(new FakeUser(true, supporter: false, UserRole.Contributor));
            var id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }
    }
}
