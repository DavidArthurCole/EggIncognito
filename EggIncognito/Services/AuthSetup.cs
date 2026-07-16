using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SyncKit.Auth;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;


public static class AuthSetup
{
   
   
    private static async Task ValidateNotRevoked(CookieValidatePrincipalContext ctx)
    {
        var identity = ctx.HttpContext.RequestServices.GetService<IdentityApiClient>();
        if (identity is null) return;
        try
        {
            await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, AuthClaims.UserIdClaim, UserRoles.ClaimType);
        }
        catch (HttpRequestException ex)
        {
            ctx.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("EggIncognito.Auth")
                .LogWarning(ex, "revocation check skipped: identity API unreachable");
        }
        catch (TaskCanceledException) { }
    }

   
   
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
