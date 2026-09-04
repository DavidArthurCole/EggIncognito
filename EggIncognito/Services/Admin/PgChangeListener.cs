using System.Collections.Concurrent;
using System.Threading.Channels;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using Npgsql;

namespace EggIncognito.Services.Admin;

public sealed class PgChangeListener(
    string connectionString,
    DeviceTimelineCache cache,
    AdminNotifier notifier,
    ILogger<PgChangeListener> logger) : BackgroundService {
    public const string ListenSql =
        "LISTEN egi_device_jobs; LISTEN egi_apks; LISTEN egi_proto_registry; LISTEN egi_staged_protos;";

    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Sweep = TimeSpan.FromMinutes(5);

    private const int KeepAliveSeconds = 30;
    private const int MaxBackoffSeconds = 30;
    private const int MaxPendingDevices = 512;

    private readonly Channel<byte> _wake =
        Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, byte> _devices = new(StringComparer.OrdinalIgnoreCase);
    private int _apks;
    private int _protos;
    private int _staged;

    public static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoffSeconds, 1 << Math.Clamp(attempt, 0, 5)));

    public static string ListenConnectionString(string source) =>
        new NpgsqlConnectionStringBuilder(source) {
            Pooling = false,
            KeepAlive = KeepAliveSeconds,
            ApplicationName = "eggincognito-listen"
        }.ConnectionString;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ListenForeverAsync(stoppingToken), PumpAsync(stoppingToken));

    private async Task ListenForeverAsync(CancellationToken ct) {
        int attempt = 0;
        bool reconnected = false;
        while (!ct.IsCancellationRequested) {
            try {
                await using var conn = new NpgsqlConnection(ListenConnectionString(connectionString));
                conn.Notification += (_, e) => OnNotification(e);
                await conn.OpenAsync(ct);
                await using var listen = new NpgsqlCommand(ListenSql, conn);
                await listen.ExecuteNonQueryAsync(ct);
                attempt = 0;
                await ReconcileAsync(reconnected, ct);
                reconnected = true;
                while (!ct.IsCancellationRequested) {
                    if (!await conn.WaitAsync(Sweep, ct)) await ReconcileAsync(false, ct);
                }
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                attempt++;
                logger.LogWarning(ex, "postgres change listener dropped, reconnecting in attempt {Attempt}", attempt);
                try {
                    await Task.Delay(BackoffFor(attempt), ct);
                } catch (OperationCanceledException) {
                    return;
                }
            }
        }
    }

    private async Task ReconcileAsync(bool announceMissed, CancellationToken ct) {
        if (await cache.RefreshMovedAsync(ct)) notifier.Publish(AdminTopics.DeviceStatus);
        if (!announceMissed) return;
        notifier.Publish(AdminTopics.Apks);
        notifier.Publish(AdminTopics.ProtoRegistry);
        notifier.Publish(AdminTopics.Staged);
    }

    private void OnNotification(NpgsqlNotificationEventArgs e) {
        if (string.Equals(e.Channel, PgChannels.DeviceJobs, StringComparison.Ordinal)) {
            if (e.Payload.Length == 0) return;
            if (_devices.Count < MaxPendingDevices) _devices[e.Payload] = 0;
        } else if (string.Equals(e.Channel, PgChannels.Apks, StringComparison.Ordinal)) {
            Volatile.Write(ref _apks, 1);
        } else if (string.Equals(e.Channel, PgChannels.ProtoRegistry, StringComparison.Ordinal)) {
            Volatile.Write(ref _protos, 1);
        } else if (string.Equals(e.Channel, PgChannels.StagedProtos, StringComparison.Ordinal)) {
            Volatile.Write(ref _staged, 1);
        } else {
            return;
        }

        _wake.Writer.TryWrite(0);
    }

    private async Task PumpAsync(CancellationToken ct) {
        try {
            while (await _wake.Reader.WaitToReadAsync(ct)) {
                Drain();
                await Task.Delay(Debounce, ct);
                Drain();
                Flush();
            }
        } catch (OperationCanceledException) {
        }
    }

    private void Drain() {
        while (_wake.Reader.TryRead(out _)) {
        }
    }

    private void Flush() {
        int touched = 0;
        foreach (string id in _devices.Keys) {
            if (!_devices.TryRemove(id, out _)) continue;
            cache.Touched(id);
            touched++;
        }

        if (touched > 0) notifier.Publish(AdminTopics.DeviceStatus);
        if (Interlocked.Exchange(ref _apks, 0) == 1) notifier.Publish(AdminTopics.Apks);
        if (Interlocked.Exchange(ref _protos, 0) == 1) notifier.Publish(AdminTopics.ProtoRegistry);
        if (Interlocked.Exchange(ref _staged, 0) == 1) notifier.Publish(AdminTopics.Staged);
    }
}
