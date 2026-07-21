using System.Net;
using Xunit;

namespace EggIncognito.Tests;

public class DataApiIntegrationTests : IClassFixture<EggIncApiFactory>
{
    private readonly EggIncApiFactory _factory;

    public DataApiIntegrationTests(EggIncApiFactory factory) => _factory = factory;

    private HttpClient Client(string ip)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("CF-Connecting-IP", ip);
        return c;
    }

    [Fact]
    public async Task Index_ListsSources()
    {
        var resp = await Client("10.10.0.1").GetAsync("/api/v1/data");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"boost\"", body);
        Assert.Contains("get_periodicals", body);
    }

    [Fact]
    public async Task Index_IsNotScrapeGated_RepeatableForAnon()
    {
        var client = Client("10.10.0.7");
        var first = await client.GetAsync("/api/v1/data");
        var second = await client.GetAsync("/api/v1/data");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedSource_Anon_Returns401()
    {
        var resp = await Client("10.10.0.2").GetAsync("/api/v1/data/periodical/get_periodicals");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PublicGamedataSource_Anon_Returns200()
    {
        var resp = await Client("10.10.0.3").GetAsync("/api/v1/data/gamedata/boost");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Extension_NestedUrl_Anon_Returns200()
    {
        var resp = await Client("10.10.0.5").GetAsync("/api/v1/data/periodical/get_periodicals/colleggtibles");
        resp.EnsureSuccessStatusCode();
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Extension_FlatUrl_Returns404()
    {
        var resp = await Client("10.10.0.6").GetAsync("/api/v1/data/periodical/colleggtibles");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownSource_Returns404()
    {
        var resp = await Client("10.10.0.4").GetAsync("/api/v1/data/nope/nope");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Anon_SecondDataCall_IsRateLimited()
    {
        var client = Client("10.10.9.9");
        var first = await client.GetAsync("/api/v1/data/gamedata/boost");
        var second = await client.GetAsync("/api/v1/data/gamedata/boost");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
