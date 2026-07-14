using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Contract;
using SyncKit.Identity.Client;
using System.Security.Claims;

namespace EggIncognito.Controllers;

// Login/logout + current-user JSON. The only login path is the SyncKit redirect flow: native provider
// buttons navigate top-level into Authentik, SyncKit redirects back to /auth/callback with a login
// code, which this app redeems into its own cookie. No Discord/Authentik OAuth wired here.
[ApiController]
public sealed class AuthController(
    AuthState authState,
    ICurrentUser currentUser,
    IServiceProvider services,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string ReturnCookie = "egi.login_return";

    // Stashes the intended post-login path in a short-lived cookie before the browser navigates away to
    // Authentik. Origin round-trips lose the path, so /auth/callback reads this back. Sent as a
    // sendBeacon text body (not JSON). Local paths only.
    [HttpPost("/auth/login-return")]
    public async Task<IActionResult> SetLoginReturn()
    {
        if (!authState.WidgetEnabled) return NotFound();
        using var reader = new StreamReader(Request.Body);
        var raw = (await reader.ReadToEndAsync()).Trim();
        // The beacon body is a JSON string literal ("/admin"); decode before validating.
        var decoded = TryJsonString(raw) ?? raw;
        var path = Url.IsLocalUrl(decoded) ? decoded : "/";
        Response.Cookies.Append(ReturnCookie, path, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(5),
        });
        return Ok();
    }

    // Redirect landing for the SyncKit mode=redirect flow. Redeems ?code into this app's cookie and
    // sends the user to their stashed return path; ?error (or any failure) lands on /?login_error=1.
    [HttpGet("/auth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error)
    {
        var identity = services.GetService<IdentityApiClient>();
        if (!authState.WidgetEnabled || identity is null) return NotFound();

        if (!string.IsNullOrEmpty(error)) return LoginFailed();
        if (string.IsNullOrWhiteSpace(code)) return BadRequest();

        RedeemLoginCodeResponse result;
        try
        {
            result = await identity.RedeemAsync(code, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "auth/callback: code redemption failed");
            return LoginFailed();
        }

        await SignInFromRedeemAsync(result);
        return Redirect(ConsumeReturnPath());
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        if (!authState.Enabled) return NotFound();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
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

    private async Task SignInFromRedeemAsync(RedeemLoginCodeResponse result)
    {
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

        var isSupporter = false;
        if (!string.IsNullOrEmpty(result.DiscordId))
        {
            var checker = services.GetRequiredService<ISupporterStatus>();
            isSupporter = await checker.CheckAsync(result.DiscordId, HttpContext.RequestAborted);
        }
        SupporterClaims.Stamp(claimsIdentity, isSupporter);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private string ConsumeReturnPath()
    {
        var path = Request.Cookies[ReturnCookie];
        Response.Cookies.Delete(ReturnCookie);
        return Url.IsLocalUrl(path) ? path! : "/";
    }

    private IActionResult LoginFailed() => Redirect("/?login_error=1");

    private static string? TryJsonString(string raw)
    {
        if (raw.Length < 2 || raw[0] != '"') return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<string>(raw); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
