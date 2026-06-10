using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

public enum AppMode { Local, Hosted }

// Whether this instance is a local self-run with full features, or a shared public host that is
// read-only: capture + endpoint writes are disabled since a request must not mutate shared data and
// the proxy cannot be shared. Driven by the `AppMode` config key, default Local, with optional
// `CaptureEnabled` / `WritesEnabled` overrides.
public interface IAppMode
{
    AppMode Mode { get; }
    bool CanCapture { get; }
    bool CanWrite { get; }
}

public sealed class AppModeService : IAppMode
{
    public AppMode Mode { get; }
    public bool CanCapture { get; }
    public bool CanWrite { get; }

    public AppModeService(IConfiguration config)
    {
        Mode = string.Equals(config["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase)
            ? AppMode.Hosted : AppMode.Local;
        var local = Mode == AppMode.Local;
        CanCapture = config.GetValue("CaptureEnabled", local);
        CanWrite = config.GetValue("WritesEnabled", local);
    }
}
