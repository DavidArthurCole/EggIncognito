using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// In-memory status for one in-flight device check-update per device. The controller fires the check as a
// background task and the UI polls this tracker via GET check-status. TryStart doubles as the overlap
// guard: a device cannot run two checks at once. Done/Error entries self-expire on read after a TTL.
public enum JobState { Running, Done, Error }

public sealed record JobStatus(
    JobState State, string Message, string? Action,
    string? InstalledBefore, string? InstalledAfter,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt);

public interface IDeviceJobTracker
{
    bool TryStart(string deviceId, string message);
    void Progress(string deviceId, string message);
    void Finish(string deviceId, StoreCheckResult result);
    void Fail(string deviceId, string note);
    JobStatus? Get(string deviceId);
}

public sealed class DeviceJobTracker(TimeProvider time) : IDeviceJobTracker
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, JobStatus> _jobs =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryStart(string deviceId, string message)
    {
        var now = time.GetUtcNow();
        var running = new JobStatus(JobState.Running, message, "running", null, null, now, now);

        // AddOrUpdate's delegate can run more than once under contention, so it stays side-effect-free.
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
        if (s.State != JobState.Running && time.GetUtcNow() - s.UpdatedAt > Ttl)
        {
            _jobs.TryRemove(deviceId, out _);
            return null;
        }
        return s;
    }
}
