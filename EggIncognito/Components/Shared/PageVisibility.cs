using EggIncognito.Services;
using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public sealed class PageVisibility(IJSRuntime js, IWebHostEnvironment env) : IAsyncDisposable {
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private DotNetObjectReference<PageVisibility>? _self;
    private Func<bool, Task>? _onChanged;

    public async Task<bool> IsVisibleAsync() {
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", InteropAsset.Url(env, "./interop/visibility.js"));
            return await _module.InvokeAsync<bool>("isVisible");
        } catch {
            return true;
        }
    }

    public async Task ListenAsync(Func<bool, Task> onChanged) {
        if (_handle is not null) return;
        _onChanged = onChanged;
        try {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", InteropAsset.Url(env, "./interop/visibility.js"));
            _self = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("listen", _self);
        } catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) {
            _onChanged = null;
        }
    }

    [JSInvokable]
    public Task OnVisibilityChanged(bool visible) => _onChanged?.Invoke(visible) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync() {
        _onChanged = null;
        try {
            if (_handle is not null) {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }
        } catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) {
            _handle = null;
        }

        await _module.DisposeModuleQuietAsync();
        _self?.Dispose();
    }
}
