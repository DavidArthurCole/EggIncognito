using System.Net;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class DynamicMockControllerTests(WebApplicationFactory<Program> f) : IClassFixture<WebApplicationFactory<Program>> {
    private sealed class FakeRoutes : IDbRouteProvider {
        private readonly RouteInfo _r = new("ei/dbonly", null, "PeriodicalsResponse", false, false, null, false, false);
        public RouteInfo? GetDbRoute(string path) => path == "ei/dbonly" ? _r : null;
        public IReadOnlyList<RouteInfo> AllDbRoutes() => [_r];
    }

    private readonly WebApplicationFactory<Program> _factory = f.WithWebHostBuilder(b => {
        b.UseSetting("NoBrowser", "true");
        b.ConfigureServices(s => {
            s.AddSingleton<IDbRouteProvider, FakeRoutes>();
            s.AddSingleton<IRouteCatalog>(sp =>
                new MergedRouteCatalog(sp.GetRequiredService<RouteCatalog>(),
                                       sp.GetRequiredService<IDbRouteProvider>()));
        });
    });

    [Fact]
    public async Task DbOnlyRoute_IsServed() {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync("/ei/dbonly", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        var bytes = Convert.FromBase64String(body);
        Assert.NotNull(Ei.PeriodicalsResponse.Parser.ParseFrom(bytes));
    }

    [Fact]
    public async Task UnknownPath_InKnownNamespace_ReturnsNotMockedMarker() {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync("/ei/does_not_exist_anywhere", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("not-mocked", resp.Headers.GetValues("x-eggincognito").Single());
    }

    [Fact]
    public async Task UnknownPath_OutsideKnownNamespaces_404s() {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync("/zz_not_auxbrain/nope", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
