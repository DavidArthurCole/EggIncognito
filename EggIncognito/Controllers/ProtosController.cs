using System.Text;
using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.ProtoExtract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("fetch")]
public sealed class ProtosController(IServiceProvider services) : ControllerBase {
    private const string FormatText = "text";
    private const string FormatUnified = "unified";
    private const string FormatSplit = "split";
    private const string FormatJson = "json";
    private static readonly string[] DiffFormats = [FormatText, FormatUnified, FormatJson, FormatSplit];
    private static readonly string[] TruthyValues = ["1", "true", "yes", "on"];

    private ProtoRegistryStore? Store =>
        services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;

    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromQuery] string? platform, CancellationToken ct) {
        if (Store is not { } store) return Ok(Array.Empty<object>());
        var rows = await store.ListAsync(platform, ct);
        var orders = await store.ShaOrdersAsync(ct);

        return Ok(rows.Select(r => new {
            r.Id,
            r.CanonicalId,
            r.Platform,
            r.AppVersion,
            r.Build,
            r.ClientVersion,
            r.Source,
            r.Package,
            r.ProtoSha,
            r.DetectedAt,
            buildFlag = ProtoVersionQuality.BuildQualityFlag(r.Platform, r.Build),
            sortOrder = orders.TryGetValue(r.ProtoSha ?? "", out int so) ? so : 0
        }));
    }

    [HttpGet("versions/{platform}/{build}")]
    public async Task<IActionResult> Get(string platform, string build, CancellationToken ct) {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return Ok(new {
            row.Platform,
            row.AppVersion,
            row.Build,
            row.ClientVersion,
            row.Source,
            row.Package,
            row.ProtoSha,
            row.DetectedAt,
            messages = pp is null ? "[]" : pp.MessageIndex,
            hasProto = pp is not null
        });
    }

    [HttpGet("versions/{platform}/{build}/proto")]
    public async Task<IActionResult> Proto(string platform, string build, CancellationToken ct) {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return pp is null ? NotFound() : Content(pp.ProtoText, "text/plain");
    }

    [HttpGet("sources")]
    public async Task<IActionResult> Sources(CancellationToken ct) =>
        Store is null ? Ok(new Dictionary<string, int>()) : Ok(await Store.SourceCountsAsync(ct));

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string platform = "android", CancellationToken ct = default) {
        if (Store is null) return NotFound();
        var rows = await Store.ListAsync(platform, ct);

        var r = rows
            .OrderByDescending(p => ProtoVersionQuality.LatestSortKey(p.Platform, p.Build, p.AppVersion))
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return r is null
            ? NotFound()
            : Ok(new { r.Platform, r.AppVersion, r.Build, r.ClientVersion, r.Source, r.ProtoSha, r.DetectedAt });
    }

    [HttpGet("diff")]
    public async Task<IActionResult> Diff(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] string platform = "android",
        [FromQuery] string? format = null, [FromQuery] int context = 3, [FromQuery] string? download = null,
        CancellationToken ct = default) {
        string fmt = string.IsNullOrWhiteSpace(format) ? FormatText : format.Trim();
        if (!DiffFormats.Contains(fmt, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "format must be one of text, unified, json, split" });

        if (Store is null) return NotFound();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "from and to required" });

        string? fromText = await LoadProtoText(platform, from, ct);
        string? toText = await LoadProtoText(platform, to, ct);
        if (fromText is null || toText is null) return NotFound();

        bool attach = Truthy(download);

        if (IsFormat(fmt, FormatText)) {
            if (attach) Attach(from, to, "txt");
            return Content(ProtoDiff.Diff(fromText, toText), "text/plain");
        }

        if (IsFormat(fmt, FormatUnified)) {
            string patch = UnifiedDiffWriter.Write(fromText, toText, new UnifiedDiffOptions(
                Math.Clamp(context, 0, 50),
                LabelA: $"{platform} {from}",
                LabelB: $"{platform} {to}"));
            if (attach) Attach(from, to, "diff");
            return Content(patch, "text/plain");
        }

        if (IsFormat(fmt, FormatSplit)) {
            var split = SideBySideDiffBuilder.Build(fromText, toText);
            if (attach) Attach(from, to, "json");
            return Ok(new { rows = split.Rows, hunkStarts = split.HunkStarts });
        }

        var structural = ProtoDiff.Compute(fromText, toText);
        var lineOps = MyersDiff.Compute(
            UnifiedDiffWriter.SplitLines(fromText), UnifiedDiffWriter.SplitLines(toText));
        if (attach) Attach(from, to, "json");
        return Ok(new { entries = structural.Entries, summary = ProtoDiffSummary.From(structural, lineOps) });
    }

    private void Attach(string from, string to, string extension) =>
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"ei-{SafeName(from)}..{SafeName(to)}.{extension}\"";

    private static bool IsFormat(string value, string name) =>
        string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

    private static bool Truthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TruthyValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string SafeName(string value) {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        return sb.Length == 0 ? "proto" : sb.ToString();
    }

    private async Task<string?> LoadProtoText(string platform, string build, CancellationToken ct) {
        var row = await Store!.GetAsync(platform, build, ct);
        if (row is null) return null;
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return pp?.ProtoText;
    }
}
