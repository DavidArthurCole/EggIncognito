using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed record EndpointRebuildResult(int Discovered, int New, int DriftCount, string? BinaryVersion, string? Note);

public sealed class EndpointCatalogRebuilder(
    IServiceProvider services,
    GameBinaryProvider binaries,
    RouteCatalog yaml) {
    public async Task<EndpointRebuildResult> RebuildAsync(CancellationToken ct) {
        var found = await binaries.GetExtractionCandidatesAsync(ct);
        if (found.Candidates.Count == 0) {
            return new EndpointRebuildResult(0, 0, 0, null,
                found.Rejected.Count == 0 ? "no extraction binary available" : found.Diagnostics);
        }

        GameBinaryProvider.ExtractionCandidate? cand = null;
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> descriptors = [];
        var failures = new List<string>(found.Rejected);

        foreach (var c in found.Candidates) {
            var syms = IsElf(c.Bytes) ? ElfSymbols.Read(c.Bytes) : c.Symbols ?? MachoSymbols.Read(c.Bytes);
            var extracted = EndpointCatalogExtractor.ExtractWith(c.Bytes, syms);
            var filtered = extracted.Ok
                ? Filter(extracted.Endpoints, yaml.ExcludedPaths)
                : [];
            if (filtered.Count > 0) {
                cand = c;
                descriptors = filtered;
                break;
            }

            failures.Add($"{c.Platform} {c.Version}: {(extracted.Ok ? "no endpoints in binary" : extracted.Diagnostics)}");
        }

        if (cand is null) return new EndpointRebuildResult(0, 0, 0, null, string.Join("; ", failures));

        var db = services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext
                 ?? throw new InvalidOperationException("no database configured");

        var now = DateTimeOffset.UtcNow;
        var existing = await db.RouteBinaryCatalogs.ToDictionaryAsync(x => x.Path, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var binaryRows = new List<BinaryRouteInfo>();

        foreach (var e in descriptors) {
            string path = e.Path!;
            if (!seen.Add(path)) continue;

            if (existing.TryGetValue(path, out var row)) {
                row.Method = e.Method;
                row.RequestType = e.RequestType;
                row.ResponseType = e.ResponseType;
                row.RequestWrapped = e.RequestWrapped;
                row.ResponseWrapped = e.ResponseWrapped;
                row.BinaryVersion = cand.Version;
                row.RefreshedAt = now;
            } else {
                db.RouteBinaryCatalogs.Add(new RouteBinaryCatalog {
                    Path = path,
                    Method = e.Method,
                    RequestType = e.RequestType,
                    ResponseType = e.ResponseType,
                    RequestWrapped = e.RequestWrapped,
                    ResponseWrapped = e.ResponseWrapped,
                    BinaryVersion = cand.Version,
                    RefreshedAt = now
                });
            }

            binaryRows.Add(new BinaryRouteInfo(path, e.Method, e.RequestType, e.ResponseType, e.RequestWrapped,
                e.ResponseWrapped, cand.Version, now));
        }

        var stale = existing.Values.Where(r => !seen.Contains(r.Path)).ToList();
        if (stale.Count > 0) db.RouteBinaryCatalogs.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
        (services.GetService(typeof(IBinaryRouteProvider)) as IBinaryRouteProvider)?.Invalidate();

        int newCount = seen.Count(p => !existing.ContainsKey(p));
        var dbRoutes = services.GetService(typeof(IDbRouteProvider)) as IDbRouteProvider;
        var overrides = services.GetService(typeof(IRouteOverrideProvider)) as IRouteOverrideProvider;
        var nonBinaryEffective = new OverlayRouteCatalog(new MergedRouteCatalog(yaml, dbRoutes), overrides).All();
        var drift = RouteDrift.Compute(nonBinaryEffective, binaryRows);
        string note = failures.Count == 0
            ? $"{cand.Platform} {cand.Version}"
            : $"{cand.Platform} {cand.Version}; skipped: {string.Join("; ", failures)}";
        return new EndpointRebuildResult(seen.Count, newCount, drift.Count, cand.Version, note);
    }

    internal static IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> Filter(
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> endpoints, IReadOnlyList<string> excludedPaths) {
        var excluded = new HashSet<string>(excludedPaths, StringComparer.Ordinal);
        return endpoints.Where(e => e.Path is not null && !excluded.Contains(e.Path)).ToList();
    }

    private static bool IsElf(byte[] b) =>
        b.Length >= 4 && b[0] == 0x7f && b[1] == 0x45 && b[2] == 0x4c && b[3] == 0x46;
}
