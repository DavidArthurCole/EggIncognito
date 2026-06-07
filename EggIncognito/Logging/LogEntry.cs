// EggIncognito/Logging/LogEntry.cs
//
// One captured log event. Seq is a process-monotonic cursor so the Inspector's Logs
// panel can poll incrementally (?since=seq) without re-fetching the whole buffer.

using Microsoft.Extensions.Logging;

namespace EggIncognito.Logging;

public sealed record LogEntry(
    long Seq,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception);
