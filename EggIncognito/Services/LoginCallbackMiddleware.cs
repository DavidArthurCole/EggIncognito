using Microsoft.AspNetCore.WebUtilities;
using SyncKit.Identity.Client;

namespace EggIncognito.Services;

// SyncKit 0.6 redirect mode appends ?code (or ?error) to the exact page the user started login from,
// not a fixed callback path. This middleware catches that on any GET: redeem the code into a cookie,
// then 302 back to the same URL with the auth params stripped so a refresh doesn't re-redeem.
public sealed class LoginCallbackMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx, AuthState authState, LoginSignIn signIn, IdentityApiClient identity, ILogger<LoginCallbackMiddleware> logger)
    {
        if (!authState.WidgetEnabled || !HttpMethods.IsGet(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var q = ctx.Request.Query;
        var code = q["code"].ToString();
        var error = q["error"].ToString();
        if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error))
        {
            await next(ctx);
            return;
        }

        if (!string.IsNullOrEmpty(code) && !(ctx.User.Identity?.IsAuthenticated ?? false))
        {
            try
            {
                var result = await identity.RedeemAsync(code, ctx.RequestAborted);
                await signIn.SignInAsync(ctx, result);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "login callback: code redemption failed");
                ctx.Response.Redirect(StripAuthParams(ctx, loginError: true));
                return;
            }
        }

        ctx.Response.Redirect(StripAuthParams(ctx, loginError: !string.IsNullOrEmpty(error)));
    }

    // Rebuilds the current URL without code/error/state, optionally re-adding login_error=1 so the
    // page can surface a failure. Keeps every other original query param.
    private static string StripAuthParams(HttpContext ctx, bool loginError)
    {
        var kept = ctx.Request.Query
            .Where(kv => kv.Key is not ("code" or "error" or "state"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (loginError) kept["login_error"] = "1";

        var path = ctx.Request.PathBase + ctx.Request.Path;
        return QueryHelpers.AddQueryString(path, kept
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));
    }
}
