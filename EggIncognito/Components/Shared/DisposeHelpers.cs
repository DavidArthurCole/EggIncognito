using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public static class DisposeHelpers {
    public static async ValueTask DisposeModuleAsync(this IJSObjectReference? module, string? teardown = null) {
        if (module is null) return;
        try {
            if (teardown is not null) await module.InvokeVoidAsync(teardown);
            await module.DisposeAsync();
        } catch (JSDisconnectedException) {
        }
    }

    public static async ValueTask DisposeModuleQuietAsync(this IJSObjectReference? module, string? teardown = null) {
        if (module is null) return;
        try {
            if (teardown is not null) await module.InvokeVoidAsync(teardown);
            await module.DisposeAsync();
        } catch {
        }
    }
}
