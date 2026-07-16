using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;
[Collection(SharedAppCollection.Name)]
public class HomePageTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public HomePageTests(SharedAppFactory f) => _factory = f;

    [Fact]
    public async Task Home_RendersFeatureGridAndSupportLink()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Inspector", html);
        Assert.Contains("/support", html);
        Assert.Contains("Getting started", html);
    }
}
