using EggIncognito.Services;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests;

public class AppModeTests {
    private static IAppMode Make(params (string, string?)[] kv) {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(kv.ToDictionary(p => p.Item1, p => p.Item2)).Build();
        return new AppModeService(config);
    }

    [Fact]
    public void Default_IsLocal_FullFeatures() {
        var m = Make();
        Assert.Equal(AppMode.Local, m.Mode);
        Assert.True(m.CanCapture);
        Assert.True(m.CanWrite);
    }

    [Fact]
    public void Hosted_DisablesCaptureAndWrites() {
        var m = Make(("AppMode", "Hosted"));
        Assert.Equal(AppMode.Hosted, m.Mode);
        Assert.False(m.CanCapture);
        Assert.False(m.CanWrite);
    }

    [Fact]
    public void ExplicitOverride_WinsOverMode() {
        var m = Make(("AppMode", "Hosted"), ("CaptureEnabled", "true"));
        Assert.True(m.CanCapture);
        Assert.False(m.CanWrite);
    }
}
