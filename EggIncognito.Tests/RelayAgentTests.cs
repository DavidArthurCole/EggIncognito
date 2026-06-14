using EggIncognito.RelayAgent;
using Xunit;

namespace EggIncognito.Tests;

public class RelayAgentTests
{
    [Fact]
    public void ProvisionArgs_RouteInto_Wg0()
    {
        var cmds = RelayCommands.Provision("2a01:4f8:c012:e15b::/64", "wg0");
        Assert.Contains(cmds, c => c.File == "ip" &&
            c.Args.Contains("route") && c.Args.Contains("2a01:4f8:c012:e15b::/64") && c.Args.Contains("wg0"));
    }

    [Fact]
    public void IsInPrefix_GuardsOutsideAddresses()
    {
        Assert.False(RelayCommands.IsInPrefix("2a01:4f8:c012:e15b::/64", "2001:db8::1"));
        Assert.True(RelayCommands.IsInPrefix("2a01:4f8:c012:e15b::/64", "2a01:4f8:c012:e15b::5"));
    }
}
