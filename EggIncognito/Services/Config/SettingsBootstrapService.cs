using EggIdentity.Settings.Store;

namespace EggIncognito.Services.Config;

public sealed class SettingsBootstrapService(
    SettingsStore store,
    SettingsCache cache,
    SettingsChangeListener listener,
    ILogger<SettingsBootstrapService> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await store.MigrateAsync(stoppingToken);
            await cache.RefreshAsync(stoppingToken);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            logger.LogSettingsBootstrapFailed(ex);
            return;
        }

        await listener.RunAsync(stoppingToken);
    }
}

internal static partial class SettingsBootstrapLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "settings store bootstrap failed; database-backed settings are unavailable this run")]
    internal static partial void LogSettingsBootstrapFailed(this ILogger logger, Exception ex);
}
