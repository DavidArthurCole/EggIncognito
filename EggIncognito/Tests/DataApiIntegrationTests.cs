using System.Net;
using EggIncognito.Services.RateLimiting;

namespace EggIncognito.Tests;

[Collection(EggIncApiCollection.Name)]
public class DataApiIntegrationTests(EggIncApiFactory factory) {
    private HttpClient Client(string ip) {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("CF-Connecting-IP", ip);
        return c;
    }

    [Fact]
    public async Task Index_ListsSources() {
        var resp = await Client("10.10.0.1").GetAsync("/api/v1/data");
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("boost-catalog", body);
        Assert.Contains("get_periodicals", body);
        Assert.DoesNotContain("season-infos", body);
    }

    [Fact]
    public async Task AuthenticatedSource_Anon_Returns401() {
        var resp = await Client("10.10.0.2").GetAsync("/api/v1/data/periodical/get_periodicals");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PublicGamedataSource_Anon_WithoutImportedDocs_Returns404() {
        var resp = await Client("10.10.0.3").GetAsync("/api/v1/data/gamedata/boost-catalog");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Extension_NestedUrl_Anon_Returns200() {
        var resp = await Client("10.10.0.5").GetAsync("/api/v1/data/periodical/get_periodicals/colleggtibles");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Extension_FlatUrl_Returns404() {
        var resp = await Client("10.10.0.6").GetAsync("/api/v1/data/periodical/colleggtibles");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownSource_Returns404() {
        var resp = await Client("10.10.0.4").GetAsync("/api/v1/data/nope/nope");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Anon_DataCalls_RateLimitedPastPolicyPermit() {
        var client = Client("10.10.9.9");
        int permit = RateLimitOptions.Defaults().Policies["DataAnon"].PermitLimit;
        for (int i = 0; i < permit; i++) {
            var resp = await client.GetAsync("/api/v1/data/gamedata/boost-catalog");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        var limited = await client.GetAsync("/api/v1/data/gamedata/boost-catalog");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
