using System.Net;

namespace EggIncognito.Tests;

[Collection(SharedAppCollection.Name)]
public class StylesBuildTests(SharedAppFactory f) {
    [Fact]
    public async Task StylesCss_IsServed_AndNonEmpty() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/styles.css");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        string css = await r.Content.ReadAsStringAsync();
        Assert.True(css.Length > 500, "compiled styles.css looks empty");
    }
}
