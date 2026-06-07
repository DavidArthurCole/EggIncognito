// EggIncognito/Logging/InMemoryLoggerProvider.cs
//
// An ILoggerProvider that mirrors every log event into the in-memory ring buffer the
// Inspector Logs panel reads. Registered alongside (not instead of) console, so adding a
// remote sink later (e.g. Serilog/Papertrail) is purely additive.

using Microsoft.Extensions.Logging;

namespace EggIncognito.Logging;

public sealed class InMemoryLoggerProvider(IInMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(store, categoryName);

    public void Dispose() { }

    private sealed class InMemoryLogger(IInMemoryLogStore store, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            store.Add(DateTimeOffset.Now, logLevel, category, message, exception?.ToString());
        }
    }
}
