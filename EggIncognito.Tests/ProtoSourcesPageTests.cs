using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Headless smoke for the backfill surface, DB-free (the test host boots without Postgres): the
// attribution page prerenders 200, and the admin-only backfill POST 403s for an anonymous caller.
public class ProtoSourcesPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public ProtoSourcesPageTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Sources_Page_Renders()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/protos/sources");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Proto sources", html);
        Assert.Contains("elgranjero/EggIncProtos", html);
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
