using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// In-memory status for one in-flight device check-update per device. The check-update poll runs ~6 minutes,
// far past the reverse-proxy timeout, so the controller fires it as a background task and the UI polls this
// tracker via GET check-status. No DB table, no SignalR: a singleton ConcurrentDictionary keyed by device id.
//
// TryStart doubles as the overlap guard: a device cannot run two checks at once. Done/Error entries self-expire
// on read after a TTL so a stale verdict clears and the row returns to idle.
public enum JobState { Running, Done, Error }

public sealed record JobStatus(
    JobState State, string Message, string? Action,
    string? InstalledBefore, string? InstalledAfter,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt);

public interface IDeviceJobTracker
{
    bool TryStart(string deviceId, string message);   // false if one is already Running
    void Progress(string deviceId, string message);
    void Finish(string deviceId, StoreCheckResult result);
    void Fail(string deviceId, string note);
    JobStatus? Get(string deviceId);                  // null => idle/expired
}

public sealed class DeviceJobTracker(TimeProvider time) : IDeviceJobTracker
{
    // How long a terminal (Done/Error) verdict lingers before a read clears it back to idle.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, JobStatus> _jobs =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryStart(string deviceId, string message)
    {
        var now = time.GetUtcNow();
        var running = new JobStatus(JobState.Running, message, "running", null, null, now, now);

        // Atomic: add if absent, else only replace a non-Running (terminal) entry. AddOrUpdate's update
        // delegate can run more than once under contention, so it must be side-effect-free; we compute the
        // winner purely from the existing value. A concurrent second caller seeing Running keeps Running, and
        // we detect that by comparing the stored value to the running one we tried to insert.
        var stored = _jobs.AddOrUpdate(
            deviceId,
            running,
            (_, existing) => existing.State == JobState.Running ? existing : running);
        return ReferenceEquals(stored, running);
    }

    public void Progress(string deviceId, string message)
    {
        var now = time.GetUtcNow();
        _jobs.AddOrUpdate(
            deviceId,
            // No live job (expired/cleared): create a Running row so progress is still visible.
            new JobStatus(JobState.Running, message, "running", null, null, now, now),
            (_, e) => e with { Message = message, UpdatedAt = now });
    }

    public void Finish(string deviceId, StoreCheckResult result)
    {
        var now = time.GetUtcNow();
        var started = _jobs.TryGetValue(deviceId, out var e) ? e.StartedAt : now;
        _jobs[deviceId] = new JobStatus(
            JobState.Done, result.Note ?? result.Action, result.Action,
            result.InstalledBefore, result.InstalledAfter, started, now);
    }

    public void Fail(string deviceId, string note)
    {
        var now = time.GetUtcNow();
        var started = _jobs.TryGetValue(deviceId, out var e) ? e.StartedAt : now;
        _jobs[deviceId] = new JobStatus(JobState.Error, note, "error", null, null, started, now);
    }

    public JobStatus? Get(string deviceId)
    {
        if (!_jobs.TryGetValue(deviceId, out var s)) return null;
        // Terminal verdicts expire on read so the row returns to idle. Running never expires here (the
        // background task always reaches Finish/Fail; a crash would leave it stuck, accepted tradeoff).
        if (s.State != JobState.Running && time.GetUtcNow() - s.UpdatedAt > Ttl)
        {
            _jobs.TryRemove(deviceId, out _);
            return null;
        }
        return s;
    }
}
