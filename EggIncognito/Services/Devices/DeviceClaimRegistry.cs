using System.Collections.Concurrent;

namespace EggIncognito.Services.Devices;

public sealed class DeviceClaimRegistry(TimeProvider time) {
    private readonly ConcurrentDictionary<string, DateTimeOffset> _claims =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Claim(string id, TimeSpan ttl) {
        var expires = time.GetUtcNow() + ttl;
        _claims[id] = expires;
        return expires;
    }

    public void Release(string id) => _claims.TryRemove(id, out _);

    public bool IsHeld(string id) {
        if (!_claims.TryGetValue(id, out var expires)) return false;
        if (expires > time.GetUtcNow()) return true;
        _claims.TryRemove(new KeyValuePair<string, DateTimeOffset>(id, expires));
        return false;
    }
}
