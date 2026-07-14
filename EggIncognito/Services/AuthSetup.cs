using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;

// Wires the single cookie scheme the SyncKit widget login signs into. No OAuth providers: login is
// SyncKit-only (AuthController.RedeemCode). Returns whether auth was wired so Program.cs can guard the
// auth middleware and endpoints. When false the app runs fully anonymous.
public static class AuthSetup
{
    private static Task ValidateNotRevoked(CookieValidatePrincipalContext ctx)
    {
        var identity = ctx.HttpContext.RequestServices.GetService<IdentityApiClient>();
        if (identity is null) return Task.CompletedTask;
        return AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, AuthClaims.UserIdClaim, UserRoles.ClaimType);
    }

    // Cookie scheme for widget-minted logins. Persistent 30-day sliding window so a login survives
    // restarts/redeploys. Registered only when the SyncKit Identity API is configured.
    public static bool AddSyncKitAuthIfConfigured(this WebApplicationBuilder builder, bool identityApiEnabled)
    {
        if (!identityApiEnabled) return false;
        builder.Services.AddAuthentication(o => o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "egi.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnValidatePrincipal = ValidateNotRevoked;
            });
        builder.Services.AddAuthorization();
        return true;
    }
}
