using EggIncognito.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public sealed class JsRouteSync(IJSRuntime js, IWebHostEnvironment env, NavigationManager nav, string prefix, ILogger logger)
    : IAsyncDisposable {
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private DotNetObjectReference<JsRouteSync>? _self;
    private string _path = "/" + Trim(nav.ToBaseRelativePath(nav.Uri));

    public event Action? Changed;

    public IReadOnlyList<string> Segments => _path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public async Task StartAsync() {
        try {
            _module = await js.InvokeAsync<IJSObjectReference>("import", InteropAsset.Url(env, "./interop/hashnav.js"));
            _self = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("listenPath", _self, prefix);
            var live = await _module.InvokeAsync<string?>("path");
            if (!string.IsNullOrEmpty(live)) _path = live;
        } catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) {
            logger.LogDebug(ex, "route synchronisation could not be wired");
        }
    }

    public void Push(string path) => _ = MoveAsync(path, replace: false);

    public void Replace(string path) => _ = MoveAsync(path, replace: true);

    [JSInvokable]
    public Task OnPathChanged(string? path) {
        if (string.IsNullOrEmpty(path) || Trim(path) == Trim(_path)) return Task.CompletedTask;
        _path = path;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private async Task MoveAsync(string path, bool replace) {
        if (Trim(path) == Trim(_path)) return;
        _path = path;
        if (_module is null) return;
        try {
            await _module.InvokeVoidAsync(replace ? "replacePath" : "pushPath", path);
        } catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) {
            logger.LogDebug(ex, "the route path could not be updated");
        }
    }

    private static string Trim(string raw) => raw.Split('?', '#')[0].Trim('/');

    public async ValueTask DisposeAsync() {
        try {
            if (_handle is not null) {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }
        } catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException) {
            logger.LogDebug(ex, "the route listener could not be released");
        }

        await _module.DisposeModuleQuietAsync();
        _self?.Dispose();
    }
}
