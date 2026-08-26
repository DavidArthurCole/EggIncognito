using EggIncognito.Services;

namespace EggIncognito.Tests;

public class BuildInfoTests {
    [Fact]
    public void Parse_WithSha_SplitsVersionAndSha() {
        var info = BuildInfo.Parse("1.2.3+abcdef0123456789", "https://github.com/EggIncTools/EggIncognito");
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal("abcdef0123456789", info.Sha);
        Assert.Equal("abcdef0", info.ShortSha);
        Assert.Equal("https://github.com/EggIncTools/EggIncognito/commit/abcdef0123456789", info.CommitUrl);
    }

    [Fact]
    public void Parse_WithoutSha_FallsBackToUnknown() {
        var info = BuildInfo.Parse("1.2.3", "https://github.com/EggIncTools/EggIncognito");
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal("unknown", info.Sha);
        Assert.Equal("unknown", info.ShortSha);
    }
}
