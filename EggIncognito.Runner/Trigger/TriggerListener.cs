using System.Security.Cryptography;
using System.Text;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Runners;

namespace EggIncognito.Runner.Trigger;

public sealed record ResyncResult(int Status, RunOutcome? Outcome, string? Error);

// The pure decision core of the resync route: bearer check, single-flight lock, outcome shaping. Kept
// separate from Kestrel so it is unit-testable without a port. run is the runner callback (force -> outcome).
public sealed class ResyncHandler(string secret, Func<bool, RunOutcome> run)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ResyncResult Handle(string? authorizationHeader, bool force)
    {
        if (!BearerMatches(authorizationHeader))
            return new ResyncResult(401, null, "unauthorized");
        if (!_lock.Wait(0))
            return new ResyncResult(409, null, "a resync is already running");
        try
        {
            var outcome = run(force);
            return new ResyncResult(200, outcome, null);
        }
        catch (Exception ex)
        {
            return new ResyncResult(500, null, ex.Message);
        }
        finally { _lock.Release(); }
    }

    private bool BearerMatches(string? header)
    {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

// Hosts the resync route on a host-local Kestrel listener. POST /resync with Authorization: Bearer
// <secret>, optional body {"force":true}. Delegates the decision to ResyncHandler.
public static class TriggerListener
{
    public static WebApplication Build(string urls, ResyncHandler handler, ApkPureExtractHandler? extract = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(urls);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapPost("/resync", async (HttpContext ctx) =>
        {
            var force = await ReadForce(ctx);
            string? auth = ctx.Request.Headers.Authorization;
            var r = handler.Handle(auth, force);
            ctx.Response.StatusCode = r.Status;
            await ctx.Response.WriteAsJsonAsync(r.Status == 200
                ? new { outcome = r.Outcome!.Detail, build = r.Outcome.Build, protoSha = r.Outcome.ProtoSha }
                : (object)new { error = r.Error });
        });
        if (extract is not null)
        {
            app.MapPost("/extract", async (HttpContext ctx) =>
            {
                var body = await ReadExtractBody(ctx);
                string? auth = ctx.Request.Headers.Authorization;
                var r = await extract.HandleAsync(auth, body?.AppVersion);
                ctx.Response.StatusCode = r.Status;
                await ctx.Response.WriteAsJsonAsync(r.Status == 200
                    ? new { build = r.Build, protoSha = r.ProtoSha, detail = r.Detail }
                    : (object)new { error = r.Error });
            });
        }
        return app;
    }

    private static async Task<bool> ReadForce(HttpContext ctx)
    {
        try
        {
            if (ctx.Request.ContentLength is > 0)
            {
                var body = await ctx.Request.ReadFromJsonAsync<ForceBody>();
                return body?.Force ?? true;
            }
        }
        catch { /* malformed body defaults to force */ }
        return true;
    }

    private static async Task<ExtractBody?> ReadExtractBody(HttpContext ctx)
    {
        try
        {
            if (ctx.Request.ContentLength is > 0)
                return await ctx.Request.ReadFromJsonAsync<ExtractBody>();
        }
        catch { /* malformed body -> null appVersion -> 400 */ }
        return null;
    }

    private sealed record ForceBody(bool Force);

    private sealed record ExtractBody(string? AppVersion);
}
