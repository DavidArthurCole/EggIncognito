using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

public sealed record RouteOverrideInfo(
    string Path,
    string? Request,
    string? Response,
    bool? RequestWrapped,
    bool? ResponseWrapped,
    bool? PathParam,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy);

public interface IRouteOverrideProvider {
    IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot();
    void Invalidate();
}

public sealed class CachedRouteOverrideProvider(
    Func<IReadOnlyDictionary<string, RouteOverrideInfo>> fetch,
    TimeSpan ttl,
    TimeProvider? time = null,
    ILogger? logger = null) : IRouteOverrideProvider {
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private IReadOnlyDictionary<string, RouteOverrideInfo> _snapshot =
        new Dictionary<string, RouteOverrideInfo>(StringComparer.Ordinal);
    private DateTimeOffset? _fetchedAt;

    public IReadOnlyDictionary<string, RouteOverrideInfo> Snapshot() {
        lock (_lock) {
            if (_fetchedAt is { } fetchedAt && _time.GetUtcNow() - fetchedAt < ttl) return _snapshot;
            Refresh();
            return _snapshot;
        }
    }

    public void Invalidate() {
        lock (_lock) {
            _fetchedAt = null;
        }
    }

    private void Refresh() {
        try {
            _snapshot = ToOrdinalDict(fetch());
        } catch (Exception ex) {
            logger?.LogSnapshotRefreshFailed(ex, nameof(RouteOverrideInfo), ttl);
        }
        _fetchedAt = _time.GetUtcNow();
    }

    private static Dictionary<string, RouteOverrideInfo> ToOrdinalDict(
        IReadOnlyDictionary<string, RouteOverrideInfo> source) {
        var result = new Dictionary<string, RouteOverrideInfo>(source.Count, StringComparer.Ordinal);
        foreach (var info in source.Values) result[info.Path] = info;
        return result;
    }
}
