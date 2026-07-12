using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;

// Wires cookie + Discord OAuth, but only when the SyncKit.Identity API is configured and both Discord
// credentials are present. Returns whether auth was wired so Program.cs can guard the auth middleware
// and endpoints. When false the app runs fully anonymous.
public static class AuthSetup
{
    private static Task ValidateNotRevoked(CookieValidatePrincipalContext ctx)
    {
        var identity = ctx.HttpContext.RequestServices.GetService<IdentityApiClient>();
        if (identity is null) return Task.CompletedTask;
        return AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, AuthClaims.UserIdClaim, UserRoles.ClaimType);
    }

    public static bool AddDiscordAuthIfConfigured(this WebApplicationBuilder builder, bool identityApiEnabled)
    {
        var clientId = builder.Configuration["Discord:ClientId"];
        var clientSecret = builder.Configuration["Discord:ClientSecret"];
        if (!identityApiEnabled || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        // IHttpContextAccessor is registered unconditionally in Program.cs; not repeated here.
        builder.Services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = DiscordAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                // Persistent cookie: 30-day sliding window so a login survives restarts/redeploys.
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "egi.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnValidatePrincipal = ValidateNotRevoked;
            })
            .AddDiscord(o =>
            {
                o.ClientId = clientId!;
                o.ClientSecret = clientSecret!;
                o.CallbackPath = "/discord-auth";
                o.SaveTokens = false;
                o.Scope.Add("identify");
                o.Events.OnCreatingTicket = async ctx =>
                {
                    ctx.Properties?.IsPersistent = true;

                    var discordId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
                    var username = ctx.Principal?.FindFirstValue(ClaimTypes.Name) ?? "";
                    var avatar = ctx.Principal?.FindFirstValue("urn:discord:avatar:hash");
                    if (!string.IsNullOrEmpty(discordId))
                    {
                        var identityClient = ctx.HttpContext.RequestServices.GetRequiredService<IdentityApiClient>();
                        var result = await identityClient.ResolveAsync(
                            "discord", discordId, discordId, username, avatar, ctx.HttpContext.RequestAborted);
                        var claimsIdentity = ctx.Identity;
                        claimsIdentity?.AddClaim(new Claim(UserRoles.ClaimType, result.Role));
                        claimsIdentity?.AddClaim(new Claim(AuthClaims.UserIdClaim, result.UserId.ToString()));
                    }

                    // Fail-closed: stamp false on any check failure; login still succeeds.
                    var checker = ctx.HttpContext.RequestServices.GetRequiredService<SupporterStatus>();
                    var isSupporter = !string.IsNullOrEmpty(discordId)
                        && await checker.CheckAsync(discordId, ctx.HttpContext.RequestAborted);
                    SupporterClaims.Stamp(ctx.Identity, isSupporter);
                };
                o.Events.OnRemoteFailure = ctx =>
                {
                    // Do not 500 on a denied/failed callback; bounce home with a benign flag.
                    ctx.Response.Redirect("/?login=failed");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();
        return true;
    }

    // Adds Authentik as a second, additive OIDC challenge scheme signing into the same cookie as
    // Discord. No-op when Authentik:Authority/ClientId/ClientSecret are unset. Safe to run whether or
    // not AddDiscordAuthIfConfigured ran first: registers the cookie scheme itself only if Discord
    // didn't already (ASP.NET Core throws on duplicate scheme names).
    public static bool AddAuthentikAuthIfConfigured(this WebApplicationBuilder builder, bool identityApiEnabled)
    {
        var authority = builder.Configuration["Authentik:Authority"];
        var clientId = builder.Configuration["Authentik:ClientId"];
        var clientSecret = builder.Configuration["Authentik:ClientSecret"];
        if (!identityApiEnabled || string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        var discordClientId = builder.Configuration["Discord:ClientId"];
        var discordClientSecret = builder.Configuration["Discord:ClientSecret"];
        var discordRegisteredCookie = identityApiEnabled && !string.IsNullOrWhiteSpace(discordClientId) && !string.IsNullOrWhiteSpace(discordClientSecret);

        var authBuilder = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (!discordRegisteredCookie)
                o.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme;
        });
        if (!discordRegisteredCookie)
        {
            // Discord didn't register the cookie scheme, so register it here for Authentik to sign in to.
            authBuilder.AddCookie(o =>
            {
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "egi.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnValidatePrincipal = ValidateNotRevoked;
            });
        }

        AuthentikAspNetAuth.AddIfConfigured(authBuilder, new AuthentikAspNetAuthOptions
        {
            CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme,
            Authority = authority!,
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            CallbackPath = "/auth-callback",
            UserIdClaim = AuthClaims.UserIdClaim,
            RoleClaim = UserRoles.ClaimType,
            OnResolved = (result, identity, _) =>
            {
                var discordId = result.DiscordId;
                if (!string.IsNullOrEmpty(discordId))
                {
                    var existingNameId = identity.FindFirst(ClaimTypes.NameIdentifier);
                    if (existingNameId is not null) identity.RemoveClaim(existingNameId);
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, discordId));
                }
                var preferredUsername = identity.FindFirst("preferred_username")?.Value;
                if (!string.IsNullOrEmpty(preferredUsername))
                {
                    var existingName = identity.FindFirst(ClaimTypes.Name);
                    if (existingName is not null) identity.RemoveClaim(existingName);
                    identity.AddClaim(new Claim(ClaimTypes.Name, preferredUsername));
                }
                return Task.CompletedTask;
            },
        });
        // Safe alongside Discord's own AddAuthorization(): registration is add-if-missing, not additive.
        builder.Services.AddAuthorization();
        return true;
    }
}
