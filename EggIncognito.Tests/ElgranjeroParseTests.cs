using EggIncognito.Services.Backfill;

namespace EggIncognito.Tests;

public class ElgranjeroParseTests
{
    [Fact]
    public void Parses_VersionCommit()
    {
        var r = ElgranjeroParse.FromMessage("ClientVersion: 72, AppVersion: 1.35.7, Build: 111343");
        Assert.NotNull(r);
        Assert.Equal("72", r!.ClientVersion);
        Assert.Equal("1.35.7", r.AppVersion);
        Assert.Equal("111343", r.Build);
    }

    [Theory]
    [InlineData("Updated workflows to add workflow_dispatch")]
    [InlineData("Merge pull request #2 from daniel-jakob/main")]
    [InlineData("Create deploy-gh-pages.yml")]
    public void Skips_NonVersionCommits(string msg) => Assert.Null(ElgranjeroParse.FromMessage(msg));
}
