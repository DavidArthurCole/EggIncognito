// Thread-safe fixed-capacity ring buffer of recent log entries, exposed to the Inspector
// Logs panel. Independent of any sink, so the file/console/remote providers stay swappable.

namespace EggIncognito.Logging;

public interface IInMemoryLogStore
{
    /// <summary>Append an entry, assigning it the next sequence number.</summary>
    LogEntry Add(DateTimeOffset ts, Microsoft.Extensions.Logging.LogLevel level,
        string category, string message, string? exception);

    /// <summary>Entries with Seq &gt; afterSeq and Level &gt;= minLevel, oldest first.</summary>
    IReadOnlyList<LogEntry> Since(long afterSeq, Microsoft.Extensions.Logging.LogLevel minLevel);
}

public sealed class InMemoryLogStore(int capacity = 2000) : IInMemoryLogStore
{
    private readonly LogEntry?[] _buffer = new LogEntry?[capacity];
    private readonly Lock _gate = new();
    private long _seq;
    private int _head; // next write index

    public LogEntry Add(DateTimeOffset ts, Microsoft.Extensions.Logging.LogLevel level,
        string category, string message, string? exception)
    {
        lock (_gate)
        {
            var entry = new LogEntry(++_seq, ts, level, category, message, exception);
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            return entry;
        }
    }

    public IReadOnlyList<LogEntry> Since(long afterSeq, Microsoft.Extensions.Logging.LogLevel minLevel)
    {
        lock (_gate)
        {
            var result = new List<LogEntry>();
            // Walk the buffer in chronological order starting at the oldest slot.
            for (var i = 0; i < _buffer.Length; i++)
            {
                var e = _buffer[(_head + i) % _buffer.Length];
                if (e is not null && e.Seq > afterSeq && e.Level >= minLevel)
                    result.Add(e);
            }
            return result;
        }
    }
}
