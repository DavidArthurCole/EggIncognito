namespace EggIncognito.Core.Services.Devices;

public static class GsfIdentity {
    public const string AndroidIdQuery =
        "content query --uri content://com.google.android.gsf.gservices "
        + "--projection value --where \"name='android_id'\" 2>/dev/null";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

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
