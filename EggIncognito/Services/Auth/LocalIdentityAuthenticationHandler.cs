using System.Security.Claims;
using System.Text.Encodings.Web;
using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EggIncognito.Services.Auth;

public sealed class LocalIdentityAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalIdentitySettings settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (ApiKeyResolutionMiddleware.HasKeyHeader(Context))
            return Task.FromResult(AuthenticateResult.NoResult());

        Claim[] claims = [
            new(AuthClaims.UserIdClaim, LocalIdentitySettings.UserId.ToString()),
            new(ClaimTypes.Name, settings.Username),
            new(AuthClaims.RoleClaim, settings.RoleName),
            new(SupporterClaims.ClaimType, settings.Supporter ? "true" : "false")
        ];
        var identity = new ClaimsIdentity(claims, LocalIdentityAuth.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), LocalIdentityAuth.Scheme)));
    }
}
