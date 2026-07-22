using EggIncognito.RelayAgent;

namespace EggIncognito.Tests;

public class RelayAgentTests {
    [Fact]
    public void ProvisionArgs_RouteInto_Wg0() {
        var cmds = RelayCommands.Provision("2001:db8::/64", "wg0");
        Assert.Contains(cmds, c => c.File == "ip" &&
            c.Args.Contains("route") && c.Args.Contains("2001:db8::/64") && c.Args.Contains("wg0"));
    }
}
