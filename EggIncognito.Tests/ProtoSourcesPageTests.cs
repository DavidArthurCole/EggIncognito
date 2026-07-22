using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class ProtoSourcesPageTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _f = f;

    [Fact]
    public async Task SourcesRoute_StillResponds() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/protos/sources");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SourcesApi_NoDb_ReturnsEmptyObject() {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/api/protos/sources");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("{}", (await r.Content.ReadAsStringAsync()).Trim());
    }
}
