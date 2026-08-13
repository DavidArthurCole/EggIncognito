using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public static class DisposeHelpers {
    public static async ValueTask DisposeModuleAsync(this IJSObjectReference? module, string? teardown = null,
        ILogger? logger = null) {
        if (module is null) return;
        try {
            if (teardown is not null) await module.InvokeVoidAsync(teardown);
            await module.DisposeAsync();
        } catch (JSDisconnectedException ex) {
            logger?.LogDebug(ex, "JS module teardown skipped, the circuit is already gone");
        }
    }

    public static async ValueTask DisposeModuleQuietAsync(this IJSObjectReference? module, string? teardown = null,
        ILogger? logger = null) {
        if (module is null) return;
        try {
            if (teardown is not null) await module.InvokeVoidAsync(teardown);
            await module.DisposeAsync();
        } catch (Exception ex) {
            logger?.LogDebug(ex, "JS module teardown failed");
        }
    }
}
