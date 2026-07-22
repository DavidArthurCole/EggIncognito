namespace EggIncognito.Services;

public enum AppMode { Local, Hosted }
public interface IAppMode {
    AppMode Mode { get; }
    bool CanCapture { get; }
    bool CanWrite { get; }


    bool HostedCaptureEnabled => false;
}

public sealed class AppModeService : IAppMode {
    public AppMode Mode { get; }
    public bool CanCapture { get; }
    public bool CanWrite { get; }
    public bool HostedCaptureEnabled { get; }

    public AppModeService(IConfiguration config) {
        Mode = string.Equals(config["AppMode"], "Hosted", StringComparison.OrdinalIgnoreCase)
            ? AppMode.Hosted : AppMode.Local;
        var local = Mode == AppMode.Local;
        CanCapture = config.GetValue("CaptureEnabled", local);
        CanWrite = config.GetValue("WritesEnabled", local);

        HostedCaptureEnabled = Mode == AppMode.Hosted && config.GetValue("HostedCaptureEnabled", false);
    }
}
