using EggIncognito.Models.Shared;

namespace EggIncognito.Services.Shared;

public sealed class ToastService : IDisposable {
    private const int MaxItems = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CollapseWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(500);

    private readonly Lock _gate = new();
    private readonly List<ToastItem> _items = [];
    private Timer? _sweep;
    private bool _disposed;

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Items {
        get {
            lock (_gate) return [.. _items];
        }
    }

    public void Push(StatusNoteKind kind, string text, string? actionLabel = null, Action? action = null) {
        if (string.IsNullOrWhiteSpace(text)) return;
        var now = DateTimeOffset.UtcNow;
        var sticky = kind == StatusNoteKind.Error || action is not null;
        lock (_gate) {
            if (_disposed) return;
            var existing = _items.FindIndex(t => t.Kind == kind && t.Text == text && now - t.At < CollapseWindow);
            if (existing >= 0) {
                _items[existing] = _items[existing] with { At = now };
            } else {
                Evict();
                _items.Add(new ToastItem(Guid.NewGuid(), kind, text, now, sticky, actionLabel, action));
            }

            if (!sticky) _sweep ??= new Timer(Sweep, null, SweepPeriod, SweepPeriod);
        }

        Changed?.Invoke();
    }

    public void Act(Guid id) {
        ToastItem? item;
        lock (_gate) item = _items.Find(t => t.Id == id);
        if (item?.Action is not { } action) return;
        Dismiss(id);
        action();
    }

    public void Dismiss(Guid id) {
        bool removed;
        lock (_gate) removed = _items.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    private void Evict() {
        while (_items.Count >= MaxItems) {
            var victim = _items.FindIndex(t => !t.Sticky);
            _items.RemoveAt(victim >= 0 ? victim : 0);
        }
    }

    private void Sweep(object? state) {
        bool changed;
        lock (_gate) {
            var cutoff = DateTimeOffset.UtcNow - Lifetime;
            changed = _items.RemoveAll(t => !t.Sticky && t.At <= cutoff) > 0;
            if (_items.TrueForAll(t => t.Sticky)) {
                _sweep?.Dispose();
                _sweep = null;
            }
        }

        if (changed) Changed?.Invoke();
    }

    public void Dispose() {
        lock (_gate) {
            _disposed = true;
            _sweep?.Dispose();
            _sweep = null;
        }
    }
}
