using EggIdentity.Auth;
using EggIdentity.Client;

namespace EggIncognito.Services;

public static class SupporterRefresh {
    public static async Task RequestAsync(HttpContext http, CancellationToken ct) {
        var api = http.RequestServices.GetService<IdentityApiClient>();
        var session = http.RequestServices.GetService<SessionCookieOptions>();
        if (api is null || session is null) return;
        if (!http.Request.Cookies.TryGetValue(session.CookieName, out string? token)) return;
        if (string.IsNullOrEmpty(token)) return;

        try {
            await api.RefreshSupporterStatusAsync(token, ct);
        } catch (Exception ex) {
            http.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("EggIncognito.Auth")
                .LogWarning(ex, "supporter refresh failed");
        }
    }
}
