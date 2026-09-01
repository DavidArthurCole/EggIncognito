namespace EggIncognito.Core.Services.Devices;

public sealed record RootAccess(bool Ok, string? SuBinary, string Detail) {
    public static readonly RootAccess None = new(false, null, "no uid=0 shell and no working su");

    public string Wrap(string command) =>
        SuBinary is null ? command : $"{SuBinary} -c {ShellQuote(command)}";

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}

public static class DeviceRoot {
    private static readonly string[] SuBinaries = ["/sbin/su", "/system/bin/su", "su"];

    public static async Task<RootAccess> ProbeAsync(IDeviceConnection conn, CancellationToken ct) {
        if (await IsRootShellAsync(conn, ct)) return new RootAccess(true, null, "adb shell is uid=0");
        return await ProbeSuAsync(conn, ct);
    }

    public static async Task<RootAccess> EnsureAsync(
        IDeviceConnection conn, IProcessRunner runner, string serial, CancellationToken ct) {
        var probed = await ProbeAsync(conn, ct);
        if (probed.Ok) return probed;

        await runner.RunAsync("adb", ["-s", serial, "root"], ct);
        await runner.RunAsync("adb", ["connect", serial], ct);
        return await ProbeAsync(conn, ct);
    }

    private static async Task<bool> IsRootShellAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync("id -u", ct);
        return r.ExitCode == 0 && r.Stdout.Trim() == "0";
    }

    private static async Task<RootAccess> ProbeSuAsync(IDeviceConnection conn, CancellationToken ct) {
        foreach (string su in SuBinaries) {
            var r = await conn.ShellAsync($"{su} -c id", ct);
            if (r.Stdout.Contains("uid=0", StringComparison.Ordinal))
                return new RootAccess(true, su, $"{su} reports uid=0");
        }

        return RootAccess.None;
    }
}
