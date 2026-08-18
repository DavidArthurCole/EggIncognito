using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedVarsTests {
    [Fact]
    public void EveryKindVar_KeepsItsNameAndOrder() {
        foreach (var kind in FeedEventKinds.All) {
            Assert.Equal(kind.Vars, FeedVars.Describe(kind).Select(v => v.Name));
        }
    }

    [Fact]
    public void ProtoKindVars_TakeTheirExamplesFromTheSampleEvent() {
        var described = FeedVars.Describe(FeedEventKinds.Proto).ToDictionary(v => v.Name, v => v.Example);
        Assert.Equal("android", described["platform"]);
        Assert.Equal("1.37.0", described["appVersion"]);
        Assert.Equal("111358", described["build"]);
        Assert.Equal("", described["flaws"]);
    }

    [Fact]
    public void ConfigKindVars_AreAllPopulatedByTheFirstSample() {
        var described = FeedVars.Describe(FeedEventKinds.Config);
        Assert.All(described, v => Assert.False(string.IsNullOrEmpty(v.Example)));
    }

    [Fact]
    public void GameDataKindVars_AreAllPopulatedByTheFirstSample() {
        var described = FeedVars.Describe(FeedEventKinds.GameData);
        Assert.All(described, v => Assert.False(string.IsNullOrEmpty(v.Example)));
    }

    [Theory]
    [InlineData("platform", "Platform")]
    [InlineData("appVersion", "App version")]
    [InlineData("prevAppVersion", "Prev app version")]
    [InlineData("sha", "Sha")]
    public void Label_SplitsCamelCase(string name, string expected) => Assert.Equal(expected, FeedVars.Label(name));
}
