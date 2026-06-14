using EggIncognito.Runner.Runners;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class IosRunnerTests
{
    [Fact]
    public void RunOnce_Throws_NotSupported()
    {
        Assert.Throws<NotSupportedException>(() => new IosRunner().RunOnce(force: true));
    }
}
