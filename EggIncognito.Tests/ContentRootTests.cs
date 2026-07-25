using EggIncognito.Services;

namespace EggIncognito.Tests;

public class ContentRootTests {
    [Fact]
    public void Resolve_PrefersConfiguredPath() {
        string dir = Path.Combine(Path.GetTempPath(), "egi-cr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "RouteMap"));
        File.WriteAllText(Path.Combine(dir, "RouteMap", "routes.yaml"), "routes:\n");
        Assert.Equal(dir, ContentRoot.Resolve(dir));
    }

    [Fact]
    public void Resolve_NullConfig_ReturnsAnExistingDirectory() {
        string resolved = ContentRoot.Resolve(null);
        Assert.False(string.IsNullOrEmpty(resolved));
        Assert.True(Directory.Exists(resolved));
    }
}
