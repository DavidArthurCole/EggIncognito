using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Headless smoke for the backfill surface, DB-free (the test host boots without Postgres): the
// attribution page prerenders 200, and the admin-only backfill POST 403s for an anonymous caller.
public class ProtoSourcesPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public ProtoSourcesPageTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    // Sources is now a modal overlay on /protos; the legacy /protos/sources route redirects there. The
    // redirect page must still respond 200 (client-side NavigateTo), not 404. The attribution content
    // itself is covered by ProtosPageTests.Component.SourcesPanel_RendersAttribution.
    [Fact]
    public async Task SourcesRoute_StillResponds()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/protos/sources");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SourcesApi_NoDb_ReturnsEmptyObject()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/api/protos/sources");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("{}", (await r.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Backfill_Elgranjero_Anonymous_Is403()
    {
        var c = _f.CreateClient();
        var r = await c.PostAsync("/api/protos/backfill/elgranjero", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, r.StatusCode);
    }
}
