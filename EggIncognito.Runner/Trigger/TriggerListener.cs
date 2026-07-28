using EggIncognito.Runner.Extract;

namespace EggIncognito.Runner.Trigger;

public static class TriggerListener {
    public static WebApplication Build(string urls, DeviceResyncHandler handler, ApkPureExtractHandler? extract = null, DeviceProbeApi? probe = null) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(urls);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapPost("/resync", async (HttpContext ctx) => {
            var force = await ReadForce(ctx);
            string? auth = ctx.Request.Headers.Authorization;
            var results = handler.HandleAll(auth, force);
            var status = results.Count == 1 ? results[0].Status : 200;
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new {
                devices = results.Select(r => new { device = r.DeviceId, status = r.Status, outcome = r.Outcome?.Detail, build = r.Outcome?.Build, protoSha = r.Outcome?.ProtoSha, error = r.Error })
            });
        });
        app.MapPost("/resync/{id}", async (HttpContext ctx, string id) => {
            var force = await ReadForce(ctx);
            string? auth = ctx.Request.Headers.Authorization;
            var r = handler.HandleOne(auth, id, force);
            ctx.Response.StatusCode = r.Status;
            await ctx.Response.WriteAsJsonAsync(r.Status == 200
                ? new { device = r.DeviceId, outcome = r.Outcome!.Detail, build = r.Outcome.Build, protoSha = r.Outcome.ProtoSha }
                : (object)new { device = r.DeviceId, error = r.Error });
        });
        if (probe is not null) {
            app.MapPost("/devices/{id}/probe", async (HttpContext ctx, string id) => {
                string? auth = ctx.Request.Headers.Authorization;
                var r = await probe.ProbeOneAsync(auth, id, "agent");
                ctx.Response.StatusCode = r.Status;
                await ctx.Response.WriteAsJsonAsync(r.Status == 200 ? r.Body! : new { device = r.DeviceId, error = r.Error });
            });
            app.MapPost("/devices/probe-all", async (HttpContext ctx) => {
                string? auth = ctx.Request.Headers.Authorization;
                var r = await probe.ProbeAllAsync(auth, "agent");
                ctx.Response.StatusCode = r.Status;
                await ctx.Response.WriteAsJsonAsync(r.Status == 200 ? r.Body! : new { error = r.Error });
            });
        }
        if (extract is not null) {
            app.MapPost("/extract", async (HttpContext ctx) => {
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

    private static async Task<bool> ReadForce(HttpContext ctx) {
        try {
            if (ctx.Request.ContentLength is > 0) {
                var body = await ctx.Request.ReadFromJsonAsync<ForceBody>();
                return body?.Force ?? true;
            }
        } catch { }
        return true;
    }

    private static async Task<ExtractBody?> ReadExtractBody(HttpContext ctx) {
        try {
            if (ctx.Request.ContentLength is > 0)
                return await ctx.Request.ReadFromJsonAsync<ExtractBody>();
        } catch { }
        return null;
    }

    private sealed record ForceBody(bool Force);

    private sealed record ExtractBody(string? AppVersion);
}
