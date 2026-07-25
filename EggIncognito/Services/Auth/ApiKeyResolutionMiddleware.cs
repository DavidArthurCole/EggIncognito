using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Authentication;

namespace EggIncognito.Services.Auth;

public sealed class ApiKeyResolutionMiddleware(RequestDelegate next) {
    public async Task Invoke(HttpContext ctx) {
        if (ctx.User.Identity?.IsAuthenticated != true && HasKeyHeader(ctx)) {
            var result = await ctx.AuthenticateAsync(ApiKeyGen.SchemeName);
            if (result.Succeeded && result.Principal is not null)
                ctx.User = result.Principal;
        }

        await next(ctx);
    }

    private static bool HasKeyHeader(HttpContext ctx) =>
        !string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Api-Key"].ToString())
        || ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer egi_", StringComparison.OrdinalIgnoreCase);
}
