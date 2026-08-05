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
        var candidates = await binaries.GetExtractionCandidatesAsync(ct);
        if (candidates.Count == 0) return new EndpointRebuildResult(0, 0, 0, null, "no extraction binary available");

        var cand = candidates[0];
        bool elf = IsElf(cand.Bytes);
        var syms = elf ? ElfSymbols.Read(cand.Bytes) : cand.Symbols ?? MachoSymbols.Read(cand.Bytes);
        var extracted = EndpointCatalogExtractor.ExtractWith(cand.Bytes, syms);
        if (!extracted.Ok) return new EndpointRebuildResult(0, 0, 0, cand.Version, extracted.Diagnostics);

        var descriptors = Filter(extracted.Endpoints, yaml.ExcludedPaths);

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
        return new EndpointRebuildResult(seen.Count, newCount, drift.Count, cand.Version,
            $"{cand.Platform} {cand.Version}");
    }

    internal static IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> Filter(
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> endpoints, IReadOnlyList<string> excludedPaths) {
        var excluded = new HashSet<string>(excludedPaths, StringComparer.Ordinal);
        return endpoints.Where(e => e.Path is not null && !excluded.Contains(e.Path)).ToList();
    }

    private static bool IsElf(byte[] b) =>
        b.Length >= 4 && b[0] == 0x7f && b[1] == 0x45 && b[2] == 0x4c && b[3] == 0x46;
}
