using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;

// Wires cookie + Discord OAuth, but only when the SyncKit.Identity API is configured and both Discord
// credentials are present. Returns whether auth was wired so Program.cs can guard the auth middleware
// and endpoints. When false the app runs fully anonymous.
public static class AuthSetup
{
    // Shared by both cookie registrations below. Only Authentik-originated tickets carry a sid claim,
    // so a Discord-only session is a no-op here; this makes back-channel logout take effect immediately
    // instead of waiting for the 30-day cookie to expire.
    private static async Task ValidateNotRevoked(CookieValidatePrincipalContext ctx)
    {
        var sid = ctx.Principal?.FindFirstValue("sid");
        if (string.IsNullOrEmpty(sid)) return;
        var identity = ctx.HttpContext.RequestServices.GetService<IdentityApiClient>();
        if (identity is null) return;
        if (await identity.IsRevokedAsync(sid, ctx.HttpContext.RequestAborted))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
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
                    if (ctx.Properties is not null) ctx.Properties.IsPersistent = true;

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
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
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
        authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
        {
            o.Authority = authority;
            o.ClientId = clientId!;
            o.ClientSecret = clientSecret!;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            // Distinct from the /auth login-start route: the OIDC middleware intercepts every request
            // to CallbackPath before MVC routing runs, so reusing /auth breaks the initial login hit.
            o.CallbackPath = "/auth-callback";
            // Authentik's default response_mode is form_post (cross-site POST), and a Lax-default
            // correlation/nonce cookie is not sent on that, so state/PKCE validation fails without None.
            o.CorrelationCookie.SameSite = SameSiteMode.None;
            o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.NonceCookie.SameSite = SameSiteMode.None;
            o.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.Scope.Add("email");
            o.Scope.Add("discord_id");
            // Without this, the handler's default inbound claim map silently renames "sub" to
            // ClaimTypes.NameIdentifier before OnTicketReceived sees the principal, breaking the raw
            // claim-name lookups elsewhere in this file (sid, discord_id, preferred_username).
            o.MapInboundClaims = false;
            // Needed so /logout can pass id_token_hint to Authentik's RP-initiated end-session endpoint.
            o.SaveTokens = true;
            o.GetClaimsFromUserInfoEndpoint = true;
            o.Events.OnTicketReceived = async ctx =>
            {
                var principal = ctx.Principal!;
                var sub = principal.FindFirstValue("sub");
                if (string.IsNullOrEmpty(sub))
                {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>().CreateLogger("AuthSetup");
                    logger.LogWarning("Authentik ticket missing sub claim; claims: {Claims}",
                        string.Join(", ", principal.Claims.Select(c => c.Type)));
                    ctx.Response.Redirect("/?login=failed");
                    ctx.HandleResponse();
                    return;
                }
                var discordId = principal.FindFirstValue("discord_id");
                var preferredUsername = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Name);
                var sp = ctx.HttpContext.RequestServices;
                var identityClient = sp.GetRequiredService<IdentityApiClient>();
                var result = await identityClient.ResolveAsync(
                    "authentik", sub, discordId, preferredUsername, avatar: null, ctx.HttpContext.RequestAborted);

                // The default inbound claim mapping already puts sub under NameIdentifier, so an
                // auto-linked discordId must replace it, not add alongside it.
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaim(new Claim(AuthClaims.UserIdClaim, result.UserId.ToString()));
                identity.AddClaim(new Claim(UserRoles.ClaimType, result.Role));
                // sid identifies this OIDC session, carried into the app cookie so a later Authentik
                // back-channel logout (which only knows the sid) can be matched to it.
                var sid = principal.FindFirstValue("sid");
                if (!string.IsNullOrEmpty(sid))
                    identity.AddClaim(new Claim("sid", sid));
                if (!string.IsNullOrEmpty(discordId))
                {
                    var existingNameId = identity.FindFirst(ClaimTypes.NameIdentifier);
                    if (existingNameId is not null) identity.RemoveClaim(existingNameId);
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, discordId));
                }
                if (!string.IsNullOrEmpty(preferredUsername))
                {
                    var existingName = identity.FindFirst(ClaimTypes.Name);
                    if (existingName is not null) identity.RemoveClaim(existingName);
                    identity.AddClaim(new Claim(ClaimTypes.Name, preferredUsername));
                }
            };
            o.Events.OnRemoteFailure = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger("AuthSetup");
                logger.LogWarning(ctx.Failure, "Authentik remote auth failure");
                ctx.Response.Redirect("/?login=failed");
                ctx.HandleResponse();
                return Task.CompletedTask;
            };
        });
        // Safe alongside Discord's own AddAuthorization(): registration is add-if-missing, not additive.
        builder.Services.AddAuthorization();
        return true;
    }
}
