namespace EggIncognito.RelayAgent;

public readonly record struct Cmd(string File, IReadOnlyList<string> Args);

public static class RelayCommands {
    public static IReadOnlyList<Cmd> Provision(string prefixCidr, string iface) =>
    [
        new("ip", ["-6", "route", "replace", prefixCidr, "dev", iface]),
    ];
}
