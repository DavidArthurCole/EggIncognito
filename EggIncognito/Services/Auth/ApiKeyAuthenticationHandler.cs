using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EggIncognito.Services.Auth;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        string? key = ExtractKey();
        if (key is null) return AuthenticateResult.NoResult();

        if (Context.RequestServices.GetService(typeof(ApiKeyStore)) is not ApiKeyStore store)
            return AuthenticateResult.NoResult();

        string hash = ApiKeyGen.HashOf(key);
        var row = await store.FindActiveByHashAsync(hash, Context.RequestAborted);
        if (row is null) return AuthenticateResult.Fail("invalid api key");

        Claim[] claims = [
            new(AuthClaims.UserIdClaim, row.OwnerUserId.ToString()),
            new(AuthClaims.RoleClaim, "viewer"),
            new(ApiKeyGen.Claim, row.Id.ToString(CultureInfo.InvariantCulture))
        ];
        var identity = new ClaimsIdentity(claims, ApiKeyGen.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        await store.TouchAsync(row.Id, Context.RequestAborted);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyGen.SchemeName));
    }

    private string? ExtractKey() {
        string header = Request.Headers["X-Api-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header.Trim();

        string auth = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) {
            string token = auth[bearer.Length..].Trim();
            if (token.StartsWith(ApiKeyGen.Scheme, StringComparison.Ordinal)) return token;
        }

        return null;
    }
}
