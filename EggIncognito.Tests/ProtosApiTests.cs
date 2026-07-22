using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ProtosApiTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task Versions_ReturnsJsonArray() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task Detail_UnknownVersion_Is404() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions/android/does-not-exist-9.9.9");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Proto_UnknownVersion_Is404() {
        using var c = _factory.CreateClient();
        var res = await c.GetAsync("/api/protos/versions/android/does-not-exist-9.9.9/proto");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }
}
