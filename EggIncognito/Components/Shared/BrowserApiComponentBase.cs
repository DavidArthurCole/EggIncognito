using EggIncognito.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EggIncognito.Components.Shared;

public abstract class BrowserApiComponentBase : ComponentBase, IAsyncDisposable {
    private IJSObjectReference? _module;

    [Inject] protected IJSRuntime Js { get; set; } = null!;
    [Inject] protected IWebHostEnvironment Env { get; set; } = null!;

    protected BrowserApi? Api { get; private set; }

    public virtual async ValueTask DisposeAsync() {
        await _module.DisposeModuleQuietAsync();
        GC.SuppressFinalize(this);
    }

    protected virtual Task OnApiReadyAsync() => Task.CompletedTask;

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender || Api is not null) return;
        try {
            _module = await Js.InvokeAsync<IJSObjectReference>("import",
                InteropAsset.Url(Env, "./interop/browserApi.js"));
        } catch (JSDisconnectedException) {
            return;
        } catch (JSException) {
            return;
        }

        Api = new BrowserApi(_module);
        await OnApiReadyAsync();
        StateHasChanged();
    }
}
