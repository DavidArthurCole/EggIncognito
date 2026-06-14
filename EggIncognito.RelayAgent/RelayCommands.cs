using System.Net;

namespace EggIncognito.RelayAgent;

public readonly record struct Cmd(string File, IReadOnlyList<string> Args);

public static class RelayCommands
{
    public static IReadOnlyList<Cmd> Provision(string prefixCidr, string iface) =>
    [
        new("ip", ["-6", "route", "replace", prefixCidr, "dev", iface]),
    ];

    public static bool IsInPrefix(string prefixCidr, string addr)
    {
        var parts = prefixCidr.Split('/');
        var prefix = IPAddress.Parse(parts[0]).GetAddressBytes();
        var len = int.Parse(parts[1]);
        if (!IPAddress.TryParse(addr, out var a)) return false;
        var ab = a.GetAddressBytes();
        if (ab.Length != prefix.Length) return false;
        var fullBytes = len / 8;
        for (var i = 0; i < fullBytes; i++) if (ab[i] != prefix[i]) return false;
        var rem = len % 8;
        if (rem != 0)
        {
            var mask = (byte)(0xFF << (8 - rem));
            if ((ab[fullBytes] & mask) != (prefix[fullBytes] & mask)) return false;
        }
        return true;
    }
}
