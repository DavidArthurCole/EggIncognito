using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Anonymous requests (no auth wired in the test host) are role viewer, so the shared-DB write gate
// (contributor+) 403s them. Reads stay public.
public class StoredEndpointGateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public StoredEndpointGateTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b
            .UseSetting("AppMode", "Hosted")
            .UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Hosted_UpsertEndpoint_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/db/endpoint",
            new { path = "ei/get_periodicals", eid = (string?)null, responseJson = "{}", responseType = "PeriodicalsResponse" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Hosted_AddRoute_Is403()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/db/route",
            new { path = "ei/new", requestType = (string?)null, responseType = "PeriodicalsResponse",
                  requestWrapped = false, responseWrapped = false, rawResponse = (string?)null,
                  pathParam = false, pathParamOnly = false });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Reads_AreReachable_EmptyWhenNoDb()
    {
        var c = _factory.CreateClient();
        var r = await c.GetAsync("/api/db/endpoints");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // [] with no DB configured
    }
}
