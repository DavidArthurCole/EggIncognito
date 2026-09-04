using EggIncognito.Capture;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services.Admin;

public sealed class CaptureCountersBridge : IHostedService, IDisposable {
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly bool _deviceCapture;
    private readonly ILogger<CaptureCountersBridge> _logger;
    private readonly IServiceProvider _services;
    private readonly Timer _timer;

    private int _armed;
    private DeviceCaptureManager? _devices;
    private AdminNotifier? _notifier;
    private CaptureSessionManager? _sessions;

    public CaptureCountersBridge(IServiceProvider services, bool deviceCapture,
        ILogger<CaptureCountersBridge> logger) {
        _services = services;
        _deviceCapture = deviceCapture;
        _logger = logger;
        _timer = new Timer(_ => Publish(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _timer.Dispose();

    public Task StartAsync(CancellationToken cancellationToken) {
        _notifier = _services.GetService<AdminNotifier>();
        if (_notifier is null) return Task.CompletedTask;

        _sessions = _services.GetService<CaptureSessionManager>();
        _sessions?.StatsChanged += Signal;

        _devices = _deviceCapture ? _services.GetService<DeviceCaptureManager>() : null;
        _devices?.CountersChanged += Signal;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _sessions?.StatsChanged -= Signal;
        _devices?.CountersChanged -= Signal;
        _sessions = null;
        _devices = null;
        _notifier = null;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    private void Signal() {
        if (Interlocked.Exchange(ref _armed, 1) == 1) return;
        try {
            _timer.Change(Window, Timeout.InfiniteTimeSpan);
        } catch (ObjectDisposedException) {
            Interlocked.Exchange(ref _armed, 0);
        }
    }

    private void Publish() {
        Interlocked.Exchange(ref _armed, 0);
        try {
            _notifier?.Publish(AdminTopics.Sessions);
        } catch (Exception ex) {
            _logger.LogDebug(ex, "capture counters: sessions publish threw");
        }
    }
}
