using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class IosRunnerTests {
    [Fact]
    public void RunOnce_MissingBinary_ReturnsFailed() {
        using var tmp = new TempDir();
        var state = new VersionState(tmp.Combine("ios-vs"));
        var runner = new IosRunner("/nonexistent/binary", state, "com.auxbrain.egginc", _ => { });
        var outcome = runner.RunOnce(force: false);
        Assert.False(outcome.Emitted);
    }
}
