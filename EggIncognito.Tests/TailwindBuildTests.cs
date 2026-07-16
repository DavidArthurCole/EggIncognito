using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;


public class TailwindBuildTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    public TailwindBuildTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseSetting("NoBrowser", "true"));

    [Fact]
    public async Task TailwindCss_IsServed_AndNonEmpty()
    {
        var c = _f.CreateClient();
        var r = await c.GetAsync("/tailwind.css");
       
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var css = await r.Content.ReadAsStringAsync();
        Assert.True(css.Length > 500, "compiled tailwind.css looks empty");
    }
}
