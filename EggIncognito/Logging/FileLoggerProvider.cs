

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Logging;

public sealed class FileLoggerProvider : ILoggerProvider {
    private readonly Channel<string> _channel =
        Channel.CreateBounded<string>(new BoundedChannelOptions(10_000) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    private readonly Task _writerTask;
    private readonly StreamWriter? _writer;
    public string? FilePath { get; }

    public FileLoggerProvider(string logsDir, string startupStamp) {
        try {
            Directory.CreateDirectory(logsDir);
            FilePath = Path.Combine(logsDir, $"eggincognito-{startupStamp}.log");
            _writer = new StreamWriter(FilePath, append: true) { AutoFlush = false };
        } catch {

            _writer = null;
        }
        _writerTask = Task.Run(DrainAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Enqueue(string line) => _channel.Writer.TryWrite(line);

    private async Task DrainAsync() {
        if (_writer is null) return;
        await foreach (var line in _channel.Reader.ReadAllAsync()) {
            await _writer.WriteLineAsync(line);

            if (_channel.Reader.Count == 0) await _writer.FlushAsync();
        }
        await _writer.FlushAsync();
    }

    public void Dispose() {
        _channel.Writer.TryComplete();
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort flush on shutdown */ }
        _writer?.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) {
            if (!IsEnabled(logLevel)) return;
            var ts = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            var line = $"{ts} [{logLevel,-11}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Enqueue(line);
        }
    }
}
