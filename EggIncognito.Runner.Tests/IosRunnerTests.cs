using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using SyncKit.Contract;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class IosRunnerTests
{
    [Fact]
    public void RunOnce_MissingBinary_ReturnsFailed()
    {
        var state = new VersionState(Path.Combine(Path.GetTempPath(), $"ios-vs-{Guid.NewGuid():N}"));
        var runner = new IosRunner("/nonexistent/binary", state, "com.auxbrain.egginc", _ => { });
        var outcome = runner.RunOnce(force: false);
        Assert.False(outcome.Emitted);
    }
}
