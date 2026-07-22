using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class InspectorSendGateTests(WebApplicationFactory<Program> f) : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory = f.WithWebHostBuilder(b => b
            .UseSetting("AppMode", "Hosted")
            .UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task Hosted_Anonymous_Send_Is403() {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/inspector/send",
            new { url = "https://www.auxbrain.com/ei/x", formBody = "data=AA==", responseType = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
