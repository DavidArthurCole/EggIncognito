using EggIncognito.Services;
using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public sealed class PageVisibility(IJSRuntime js, IWebHostEnvironment env) : IAsyncDisposable {
    private IJSObjectReference? _module;

    public async Task<bool> IsVisibleAsync() {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", InteropAsset.Url(env, "./interop/visibility.js"));
            return await _module.InvokeAsync<bool>("isVisible");
        } catch {
            return true;
        }
    }

    public ValueTask DisposeAsync() => _module.DisposeModuleQuietAsync();
}
