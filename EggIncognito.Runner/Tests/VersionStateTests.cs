using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class VersionStateTests {
    [Fact]
    public void LastSeen_AbsentFileIsEmpty() {
        using var tmp = new TempDir();
        Assert.Equal("", new VersionState(tmp.Combine("vs")).LastSeen());
    }

    [Fact]
    public void Save_ThenLastSeen_RoundTrips() {
        using var tmp = new TempDir();
        string path = tmp.Combine("vs");
        var vs = new VersionState(path);
        vs.Save("111343");
        Assert.Equal("111343", new VersionState(path).LastSeen());
    }
}
