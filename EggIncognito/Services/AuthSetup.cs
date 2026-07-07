using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EggIncognito.Services;

// Wires cookie + Discord OAuth, but only when a DB is configured and both Discord credentials are
// present. Returns whether auth was wired so Program.cs can guard the auth middleware + endpoints.
// When false the app runs fully anonymous: login 404s, /api/app/mode reports authEnabled:false.
public static class AuthSetup
{
    public static bool AddDiscordAuthIfConfigured(this WebApplicationBuilder builder, bool dbEnabled)
    {
        var clientId = builder.Configuration["Discord:ClientId"];
        var clientSecret = builder.Configuration["Discord:ClientSecret"];
        if (!dbEnabled || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
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
                    await UserUpsert.OnLoginAsync(ctx);
                    // Fail-closed: stamp false on any check failure; login still succeeds.
                    var checker = ctx.HttpContext.RequestServices.GetRequiredService<SupporterStatus>();
                    var discordId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
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
    // Discord. No-op (returns false, wires nothing) when Authentik:Authority/ClientId/ClientSecret
    // are unset, so a deploy with no Authentik app/provider configured yet behaves exactly as
    // before. Independently safe to run whether or not AddDiscordAuthIfConfigured ran first: if
    // Discord didn't register the cookie scheme (unconfigured, or this runs standalone in a test),
    // this method registers it itself so Authentik's SignInScheme has somewhere to land. If Discord
    // DID already register "Cookies", this must NOT call AddCookie again (ASP.NET Core throws on
    // duplicate scheme names), so the registration is gated on the same two config keys
    // AddDiscordAuthIfConfigured itself gates on.
    public static bool AddAuthentikAuthIfConfigured(this WebApplicationBuilder builder, bool dbEnabled)
    {
        var authority = builder.Configuration["Authentik:Authority"];
        var clientId = builder.Configuration["Authentik:ClientId"];
        var clientSecret = builder.Configuration["Authentik:ClientSecret"];
        if (!dbEnabled || string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        var discordClientId = builder.Configuration["Discord:ClientId"];
        var discordClientSecret = builder.Configuration["Discord:ClientSecret"];
        var discordRegisteredCookie = dbEnabled && !string.IsNullOrWhiteSpace(discordClientId) && !string.IsNullOrWhiteSpace(discordClientSecret);

        var authBuilder = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (!discordRegisteredCookie)
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        });
        if (!discordRegisteredCookie)
        {
            // Discord didn't register the cookie scheme (either unconfigured or this method ran
            // standalone in a test): register it here so Authentik has somewhere to sign in to.
            authBuilder.AddCookie(o =>
            {
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "egi.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });
        }
        authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
        {
            o.Authority = authority;
            o.ClientId = clientId!;
            o.ClientSecret = clientSecret!;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.CallbackPath = "/auth";
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.Scope.Add("email");
            o.Scope.Add("discord_id");
            o.SaveTokens = false;
            o.GetClaimsFromUserInfoEndpoint = true;
            o.Events.OnTicketReceived = async ctx =>
            {
                var principal = ctx.Principal!;
                var sub = principal.FindFirstValue("sub");
                if (string.IsNullOrEmpty(sub))
                {
                    ctx.Response.Redirect("/?login=failed");
                    ctx.HandleResponse();
                    return;
                }
                var discordId = principal.FindFirstValue("discord_id");
                var sp = ctx.HttpContext.RequestServices;
                var resolver = sp.GetRequiredService<AuthentikIdentityResolver>();
                var userId = await resolver.ResolveAsync(sub, discordId, ctx.HttpContext.RequestAborted);

                // Stamp the resolved user's stored role/username so an Authentik login carries the
                // same authority + display name a Discord login would, instead of reverting to a
                // rightless, nameless viewer. The default inbound claim mapping already puts sub
                // under NameIdentifier, so an auto-linked discordId must replace it, not add
                // alongside it, or FindFirstValue(NameIdentifier) keeps returning sub.
                var db = sp.GetRequiredService<EggIncognitoDbContext>();
                var user = await db.Users.FirstAsync(u => u.UserId == userId, ctx.HttpContext.RequestAborted);
                var preferredUsername = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(user.Username) && !string.IsNullOrEmpty(preferredUsername))
                {
                    user.Username = preferredUsername;
                    await db.SaveChangesAsync(ctx.HttpContext.RequestAborted);
                }

                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaim(new Claim(AuthClaims.UserIdClaim, userId.ToString()));
                UserUpsert.StampRoleClaim(identity, user.Role);
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
                ctx.Response.Redirect("/?login=failed");
                ctx.HandleResponse();
                return Task.CompletedTask;
            };
        });
        // Safe to call alongside Discord's own AddAuthorization(): ASP.NET Core's authorization
        // service registration is add-if-missing, not additive, so this is a no-op when Discord
        // already registered it and the only registration when Authentik runs standalone.
        builder.Services.AddAuthorization();
        return true;
    }
}
