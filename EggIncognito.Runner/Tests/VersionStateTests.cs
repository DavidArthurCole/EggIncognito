using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class VersionStateTests
{
    [Fact]
    public void LastSeen_AbsentFileIsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vs-{Guid.NewGuid():N}");
        Assert.Equal("", new VersionState(path).LastSeen());
    }

    [Fact]
    public void Save_ThenLastSeen_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vs-{Guid.NewGuid():N}");
        try
        {
            var vs = new VersionState(path);
            vs.Save("111343");
            Assert.Equal("111343", new VersionState(path).LastSeen());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
