namespace EggIncognito.Components.Shared;

public static class ComponentTimers {
    public static Timer Every(TimeSpan period, Func<Task> tick, ILogger? logger = null) =>
        Every(period, period, tick, logger);

    public static Timer Every(TimeSpan due, TimeSpan period, Func<Task> tick, ILogger? logger = null) =>
        new(async _ => {
            try {
                await tick();
            } catch (Exception ex) {
                logger?.LogDebug(ex, "component timer tick failed");
            }
        }, null, due, period);
}
