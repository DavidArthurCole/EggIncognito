using System.Security.Claims;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SyncKit.Auth;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;


public static class AuthSetup {


    private static async Task ValidateNotRevoked(CookieValidatePrincipalContext ctx) {
        var identity = ctx.HttpContext.RequestServices.GetService<IdentityApiClient>();
        if (identity is null) return;
        try {
            await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, AuthClaims.UserIdClaim, AuthClaims.RoleClaim);
        } catch (HttpRequestException ex) {
            ctx.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("EggIncognito.Auth")
                .LogWarning(ex, "revocation check skipped: identity API unreachable");
        } catch (TaskCanceledException) { }
    }

    private static async Task StampSupporterClaim(ClaimsPrincipal principal, HttpContext ctx, CancellationToken ct) {
        var discordId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(SessionClaims.DiscordId);
        if (string.IsNullOrEmpty(discordId) || principal.Identity is not ClaimsIdentity identity) return;
        var supporters = ctx.RequestServices.GetService<ISupporterStatus>();
        if (supporters is null) return;

        bool isSupporter;
        try {
            isSupporter = await supporters.CheckAsync(discordId, ct);
        } catch (Exception ex) {
            ctx.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("EggIncognito.Auth")
                .LogWarning(ex, "supporter check skipped during claims validation");
            return;
        }

        if (identity.FindFirst(SupporterClaims.ClaimType) is { } existing) identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(SupporterClaims.ClaimType, isSupporter ? "true" : "false"));
    }

    private static Task StampSupporterClaimCookie(CookieValidatePrincipalContext ctx) =>
        ctx.Principal is null ? Task.CompletedTask : StampSupporterClaim(ctx.Principal, ctx.HttpContext, ctx.HttpContext.RequestAborted);

    public static bool AddSyncKitAuthIfConfigured(
        this WebApplicationBuilder builder, bool identityApiEnabled, SessionCookieOptions? session) {
        if (!identityApiEnabled) return false;
        var auth = builder.Services.AddAuthentication(session is not null
                ? SelectorScheme
                : CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o => {
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "egi.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnValidatePrincipal = async ctx => {
                    await ValidateNotRevoked(ctx);
                    await StampSupporterClaimCookie(ctx);
                };
            })
            .AddScheme<AuthenticationSchemeOptions, Auth.ApiKeyAuthenticationHandler>(
                DataApi.ApiKeyGen.SchemeName, null);
        if (session is not null) {
            auth.AddSyncKitSession(session, onValidated: StampSupporterClaim);
            auth.AddPolicyScheme(SelectorScheme, SelectorScheme, o =>
                o.ForwardDefaultSelector = ctx =>
                    ctx.Request.Cookies.ContainsKey(session.CookieName)
                        ? SyncKitSessionDefaults.Scheme
                        : CookieAuthenticationDefaults.AuthenticationScheme);
        }
        builder.Services.AddAuthorization();
        return true;
    }

    private const string SelectorScheme = "EgiAuthSelector";
}
