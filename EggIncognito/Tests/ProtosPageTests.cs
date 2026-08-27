using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Pages;
using EggIncognito.Services;
using EggIncognito.Services.Api;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Notifications;
using EggIncognito.Services.Protos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class ProtosPageTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Protos_Anonymous_RendersTableShell_NoBackfillPanel() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/protos");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string html = await r.Content.ReadAsStringAsync();
            Assert.Contains("pd-grid", html);
            Assert.Contains(">Versions</h3>", html);

            Assert.DoesNotContain("id=\"backfillPanel\"", html);
        }


        [Fact]
        public async Task ProtoDataRoute_StillResponds() =>
            Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protodata")).StatusCode);

        [Fact]
        public async Task SubscribeRoute_StillResponds() =>
            Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/subscribe")).StatusCode);
    }


    public class Component : BunitContext {
        private void Wire(UserRole role) {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton(new AuthState(false));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddSingleton<IRouteCatalog>(new RouteCatalog("__no_routes_yaml__"));
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            Services.AddSingleton<IAppMode>(new FakeAppMode());
            Services.AddSingleton<ISealedProxy>(new FakeSealedProxy());
            Services.AddScoped<ProtoWorkbenchState>();
            Services.AddScoped<DeviceWorkbenchState>();
            Services.AddScoped<NotificationsWorkbenchState>();
            Services.AddScoped<ApiWorkbenchState>();
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

    private sealed class FakeAppMode : IAppMode {
        public AppMode Mode => AppMode.Local;
        public bool CanCapture => false;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => false;
    }

    private sealed class FakeSealedProxy : ISealedProxy {
        public bool IsConfigured => false;
        public Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default) => Task.FromResult(false);
        public HttpClient CreateEgressClient() => new();
    }
}
