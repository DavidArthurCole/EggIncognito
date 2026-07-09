using AspNet.Security.OAuth.Discord;
using EggIncognito.Data.Services;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SyncKit.Contract;
using SyncKit.Identity.Client;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. AuthState.DiscordEnabled/AuthentikEnabled gate each provider's
// own challenge endpoint so one running without the other 404s instead of throwing against an
// unregistered scheme.
[ApiController]
public sealed class AuthController(
    AuthState authState,
    ICurrentUser currentUser,
    IConfiguration config,
    IServiceProvider services,
    ILogger<AuthController> logger) : ControllerBase
{
    private bool AuthOn => authState.Enabled;

    [HttpGet("/login")]
    public IActionResult Login([FromQuery] string returnUrl = "/")
    {
        if (!authState.DiscordEnabled) return NotFound();
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            DiscordAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet("/auth")]
    public IActionResult AuthentikLogin([FromQuery] string returnUrl = "/")
    {
        if (!authState.AuthentikEnabled) return NotFound();
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    // Embedded popup widget: exchanges a short-lived SyncKit login code for this app's own cookie,
    // minting the exact same claim shape AuthSetup's Discord/Authentik OnCreatingTicket/OnTicketReceived
    // handlers do. No "sid" claim: this session did not come through an OIDC ticket, so back-channel
    // logout (which matches on sid) does not apply to it.
    [HttpPost("/auth/redeem-code")]
    public async Task<IActionResult> RedeemCode([FromBody] RedeemCodeBody body)
    {
        var identity = services.GetService<IdentityApiClient>();
        if (!authState.WidgetEnabled || identity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(body.Code)) return BadRequest();

        RedeemLoginCodeResponse result;
        try
        {
            result = await identity.RedeemAsync(body.Code, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "redeem-code: code redemption failed");
            return BadRequest();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.DiscordId ?? result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(UserRoles.ClaimType, result.Role),
            new(AuthClaims.UserIdClaim, result.UserId.ToString()),
        };
        if (!string.IsNullOrEmpty(result.Avatar))
            claims.Add(new Claim("urn:discord:avatar:hash", result.Avatar));
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(claimsIdentity);

        var isSupporter = false;
        if (!string.IsNullOrEmpty(result.DiscordId))
        {
            var checker = services.GetRequiredService<SupporterStatus>();
            isSupporter = await checker.CheckAsync(result.DiscordId, HttpContext.RequestAborted);
        }
        SupporterClaims.Stamp(claimsIdentity, isSupporter);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });

        return Ok(new { discordId = result.DiscordId, username = result.Username, avatar = result.Avatar });
    }

    public sealed record RedeemCodeBody(string Code);

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        if (!AuthOn) return NotFound();
        // The OIDC handler needs the cookie still live to read id_token during its own SignOut,
        // so Cookies must be included in the SAME SignOut call, not signed out beforehand.
        var idToken = await HttpContext.GetTokenAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, "id_token");
        if (authState.AuthentikEnabled && !string.IsNullOrEmpty(idToken))
        {
            var props = new AuthenticationProperties { RedirectUri = "/" };
            return SignOut(props,
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    // Authentik back-channel logout notification: server-to-server POST with a signed logout_token,
    // no cookies/session context. Per OIDC Back-Channel Logout 1.0 sec 2.6: verify signature + iss/aud,
    // require an "events" claim carrying backchannel-logout, forbid a "nonce" claim, then revoke the sid.
    [HttpPost("/auth/backchannel-logout")]
    [AllowAnonymous]
    public async Task<IActionResult> BackchannelLogout([FromForm] string? logout_token)
    {
        var oidcConfig = services.GetService<ConfigurationManager<OpenIdConnectConfiguration>>();
        var identity = services.GetService<IdentityApiClient>();
        if (!authState.AuthentikEnabled || oidcConfig is null || identity is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(logout_token))
            return BadRequest();

        OpenIdConnectConfiguration discovery;
        try
        {
            discovery = await oidcConfig.GetConfigurationAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backchannel-logout: could not fetch Authentik discovery/JWKS");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var validationParams = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            IssuerSigningKeys = discovery.SigningKeys,
            ValidAudience = config["Authentik:ClientId"],
            ValidateLifetime = true,
        };

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(logout_token, validationParams, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backchannel-logout: logout_token failed validation");
            return BadRequest();
        }

        if (principal.FindFirstValue("nonce") is not null)
            return BadRequest();

        var events = principal.FindFirstValue("events");
        if (string.IsNullOrEmpty(events) || !events.Contains("backchannel-logout"))
            return BadRequest();

        var sid = principal.FindFirstValue("sid");
        if (string.IsNullOrEmpty(sid))
            return BadRequest();

        await identity.RevokeSessionAsync(sid, HttpContext.RequestAborted);
        return Ok();
    }

    [HttpGet("/api/auth/me")]
    public IActionResult Me()
    {
        if (!currentUser.IsAuthenticated)
            return Ok(new { authenticated = false });
        return Ok(new
        {
            authenticated = true,
            discordId = currentUser.DiscordId,
            username = currentUser.Username,
            avatar = currentUser.Avatar,
        });
    }
}
