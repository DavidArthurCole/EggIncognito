using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EggIncognito.Components.Shared;

public static class RenderModes {
    public static readonly IComponentRenderMode InteractiveServer = new InteractiveServerRenderMode(prerender: false);
}
