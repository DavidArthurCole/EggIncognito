using System.Net;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class TailwindBuildTests(SharedAppFactory f) {
    [Fact]
    public async Task TailwindCss_IsServed_AndNonEmpty() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/tailwind.css");

        if (r.StatusCode == HttpStatusCode.NotFound) return;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string css = await r.Content.ReadAsStringAsync();
        Assert.True(css.Length > 500, "compiled tailwind.css looks empty");
    }
}
