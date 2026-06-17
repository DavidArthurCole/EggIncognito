using System.Security.Claims;
using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

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
}
