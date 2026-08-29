using System.Net;
using EggIncognito.Core.Services;
using Ei;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public sealed class DynamicMockFactory : EgiTestFactory {
    protected override void Configure(IWebHostBuilder builder) =>
        builder.ConfigureServices(s => {
            s.AddSingleton<IDbRouteProvider, FakeRoutes>();
            s.AddSingleton<IRouteCatalog>(sp =>
                new MergedRouteCatalog(sp.GetRequiredService<RouteCatalog>(),
                    sp.GetRequiredService<IDbRouteProvider>()));
        });

    internal sealed class FakeRoutes : IDbRouteProvider {
        private readonly RouteInfo _r = new("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false);
        public RouteInfo? GetDbRoute(string path) => path == "ei/dbonly" ? _r : null;
        public IReadOnlyList<RouteInfo> AllDbRoutes() => [_r];
        public void Invalidate() {
        }
    }
}

public class DynamicMockControllerTests(DynamicMockFactory f) : IClassFixture<DynamicMockFactory> {

    [Fact]
    public async Task DbOnlyRoute_IsServed() {
        var c = f.CreateClient();
        var resp = await c.PostAsync("/ei/dbonly", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        byte[] bytes = Convert.FromBase64String(body);
        Assert.NotNull(PeriodicalsResponse.Parser.ParseFrom(bytes));
    }

    [Fact]
    public async Task UnknownPath_InKnownNamespace_ReturnsNotMockedMarker() {
        var c = f.CreateClient();
        var resp = await c.PostAsync("/ei/does_not_exist_anywhere", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("not-mocked", resp.Headers.GetValues("x-eggincognito").Single());
    }

    [Fact]
    public async Task UnknownPath_OutsideKnownNamespaces_404s() {
        var c = f.CreateClient();
        var resp = await c.PostAsync("/zz_not_auxbrain/nope", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
