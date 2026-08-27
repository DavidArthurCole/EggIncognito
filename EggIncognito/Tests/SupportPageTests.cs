using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class SupportPageTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task Support_Route_IsARedirectStub() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/support");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        string html = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("support-lede", html);
        Assert.DoesNotContain("support-section", html);
        Assert.DoesNotContain("perk-list", html);
        Assert.Contains("id=\"siteFooter\"", html);
    }
}
