namespace EggIncognito.Core.Services.Devices;

public static class GsfIdentity {
    public const string AndroidIdQuery =
        "content query --uri content://com.google.android.gsf.gservices "
        + "--projection value --where \"name='android_id'\" 2>/dev/null";

    public const string CheckinCommand =
        "am broadcast -a android.server.checkin.CHECKIN -n com.google.android.gms/.checkin.CheckinService >/dev/null 2>&1; "
        + "am broadcast -a android.server.checkin.CHECKIN >/dev/null 2>&1; echo kicked=1";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public static async Task<bool> KickAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(CheckinCommand), ct);
        return r.Stdout.Contains("kicked=1", StringComparison.Ordinal);
    }

    public static async Task<string?> ReadAsync(IDeviceConnection conn, CancellationToken ct) {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(QueryTimeout);
        var r = await conn.ShellAsync(AndroidIdQuery, bounded.Token);
        ct.ThrowIfCancellationRequested();
        return Parse(r.Stdout);
    }

    public static async Task<string?> WaitAsync(
        IDeviceConnection conn, TimeSpan timeout, TimeSpan interval, CancellationToken ct) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true) {
            if (await ReadAsync(conn, ct) is { } id) return id;
            if (DateTimeOffset.UtcNow >= deadline) return null;
            await Task.Delay(interval, ct);
        }
    }

    public static string? Parse(string stdout) {
        foreach (string line in stdout.Split('\n')) {
            int at = line.IndexOf("value=", StringComparison.Ordinal);
            if (at < 0) continue;
            string value = line[(at + "value=".Length)..].Trim();
            if (value.Length > 0 && !value.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return value;
        }

        return null;
    }
}
