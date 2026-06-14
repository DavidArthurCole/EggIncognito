using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// Public proto registry API surface. Robust to both run environments: no Postgres (store absent) and a
// dev box with a real DB. versions always returns a JSON array; an unknown (platform, version) is
// always 404 whether or not a DB is present. DB-backed content is covered by ingest + store tests.
public class ProtosApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ProtosApiTests(WebApplicationFactory<Program> f) =>
        _factory = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Versions_ReturnsJsonArray()
    {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task Detail_UnknownVersion_Is404()
    {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions/android/does-not-exist-9.9.9");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Proto_UnknownVersion_Is404()
    {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions/android/does-not-exist-9.9.9/proto");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }
}
