using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class HomePageTests(SharedAppFactory f) {
    private readonly WebApplicationFactory<Program> _factory = f;

    [Fact]
    public async Task Home_RendersHeaderAndSupportLink() {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/");
        res.EnsureSuccessStatusCode();
        string html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Inspector", html);
        Assert.Contains("/support", html);
    }
}
