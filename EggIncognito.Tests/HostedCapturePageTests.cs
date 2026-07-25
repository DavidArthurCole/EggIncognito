using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CapturePage = EggIncognito.Components.Pages.Capture;

namespace EggIncognito.Tests;

public class HostedCapturePageTests {
    private static CaptureSessionManager NewManager() =>
        new(HostedCaptureOptions.Defaults(),
            (key, basePort) => CaptureSessionManagerTests.NewSession(
                key == CaptureSessionManager.LocalKey ? 18080 : basePort));

    private sealed class FakeAppMode(bool canCapture, bool hostedEnabled) : IAppMode {
        public AppMode Mode => AppMode.Hosted;
        public bool CanCapture => canCapture;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => hostedEnabled;
    }

    private sealed class FakeUser(bool authed, bool supporter, UserRole role = UserRole.Viewer) : ICurrentUser {
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

    private sealed class FakeSupporters(bool result) : ISupporterStatus {
        public Task<bool> CheckAsync(string discordId, CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeRoutes : IRouteCatalog {
        public IReadOnlyList<RouteInfo> All() => [];
        public RouteInfo? Get(string path) => new(path, null, "PeriodicalsResponse", false, false, null, false, false);
    }


    [Collection(HostedAppCollection.Name)]
    public class HostedDefault(HostedAppFactory f) {
        [Fact]
        public async Task Mode_Hosted_Default_ReportsHostedCaptureFalse() {
            string json = await f.CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":false", json);
        }
    }

    [Collection(HostedCaptureAppCollection.Name)]
    public class CaptureEnabled(HostedCaptureAppFactory f) {
        [Fact]
        public async Task Mode_Hosted_Enabled_ReportsHostedCaptureTrue() {
            string json = await f.CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":true", json);
        }

        [Fact]
        public async Task CapturePage_HostedEnabled_Anonymous_ShowsLoginPrompt() {
            string html = await f.CreateClient().GetStringAsync("/capture");
            Assert.Contains("id=\"hostedLogin\"", html);

            Assert.Contains("Login unavailable", html);
            Assert.DoesNotContain("id=\"statsPanel\"", html);
        }

        [Fact]
        public async Task CaptureStart_HostedEnabled_Anonymous_Is401() {
            var r = await f.CreateClient().PostAsync("/api/capture/start", null);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }


    public class Component : BunitContext {
        private void Wire(bool authed, bool supporter) {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<IAppMode>(new FakeAppMode(false, true));
            Services.AddSingleton<ICurrentUser>(new FakeUser(authed, supporter));
            Services.AddSingleton(HostedCaptureOptions.Defaults());
            Services.AddSingleton(NewManager());
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Services.AddSingleton(new AuthState(false));
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsLoginPrompt() {
            Wire(false, false);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedLogin"));
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
        }

        [Fact]
        public void NonSupporter_ShowsPitchWithSupportLink() {
            Wire(true, false);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedPitch"));
            Assert.Contains("href=\"/support\"", cut.Markup);
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
        }

        [Fact]
        public void Supporter_ShowsSetupCard_AndDashboard() {
            Wire(true, true);
            var cut = Render<CapturePage>();
            Assert.NotNull(cut.Find("#hostedSetupCard"));
            Assert.NotNull(cut.Find("#statsPanel"));
        }

        [Fact]
        public void Supporter_SetupCard_ShowsProxyAddress_NoAuthCredentials() {
            Wire(true, true);
            var cut = Render<CapturePage>();
            string markup = cut.Markup;

            Assert.NotNull(cut.Find("#proxyHost"));
            Assert.Contains("Server", markup);
            Assert.Contains("Port", markup);
            Assert.Contains("Auth", markup);

            Assert.Empty(cut.FindAll("#proxyProfileCard"));
            Assert.Empty(cut.FindAll("#mintedToken"));
            Assert.DoesNotContain("Username", markup);
            Assert.DoesNotContain("Token", markup);
            Assert.DoesNotContain("Cycle token", markup);
            Assert.DoesNotContain("SSID", markup);
            Assert.DoesNotContain("DM proxy profile", markup);
        }
    }


    public class StartGate {
        private static CaptureController Controller(
            CaptureSessionManager manager, ICurrentUser user, ISupporterStatus supporters) =>
            new(manager, new FakeAppMode(false, true), user, supporters,
                HostedCaptureOptions.Defaults(), new EmptyServices());

        [Fact]
        public async Task Start_Anonymous_Is401() {
            var r = await Controller(NewManager(), new FakeUser(false, false), new FakeSupporters(true))
                .Start(CancellationToken.None);
            Assert.Equal(401, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Start_LiveCheckFails_Is403SupporterRequired() {
            var r = await Controller(NewManager(), new FakeUser(true, true), new FakeSupporters(false))
                .Start(CancellationToken.None);
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
            Assert.Contains("supporter_required", ((ObjectResult)r).Value!.ToString());
        }

        [Fact]
        public async Task Start_Supporter_StartsOwnSession() {
            var manager = NewManager();
            var r = await Controller(manager, new FakeUser(true, true), new FakeSupporters(true))
                .Start(CancellationToken.None);
            Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
            var session = manager.Get("tester");
            Assert.NotNull(session);
            Assert.Equal(CaptureState.Running, session.State);
            await session.StopAsync();
        }

        [Fact]
        public async Task ProxyAddress_NonSupporter_Is403() {
            var r = await Controller(NewManager(), new FakeUser(true, false), new FakeSupporters(false))
                .ProxyAddress(CancellationToken.None);
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
        }
    }

    public class SaveGate {
        private static (CaptureController Controller, CaptureSession Session) WithFlowSession(ICurrentUser user) {
            var manager = NewManager();
            var session = manager.GetOrCreate("tester");
            var controller = new CaptureController(
                manager, new FakeAppMode(false, true), user,
                new FakeSupporters(true), HostedCaptureOptions.Defaults(), new EmptyServices());
            return (controller, session);
        }

        private static long PublishFlow(CaptureSession session) {
            var stored = session.Hub.Publish(
                new DashboardFlow(0, "", "ei/first_contact", "POST", 200, null, null, "AA==", null),
                "12:00:00");
            return stored!.Id;
        }

        [Fact]
        public async Task Save_Hosted_ViewerNonSupporter_Is403() {
            var (controller, session) = WithFlowSession(new FakeUser(true, false));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Save_Hosted_Supporter_PassesGate_Then503NoDb() {
            var (controller, session) = WithFlowSession(new FakeUser(true, true));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Save_Hosted_Contributor_PassesGate_Then503NoDb() {
            var (controller, session) = WithFlowSession(new FakeUser(true, false, UserRole.Contributor));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new CaptureController.SaveEndpointRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }
    }
}
