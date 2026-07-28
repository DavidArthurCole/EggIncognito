using System.Text;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Services.ProtoExtract;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed record RebuildDocResult(string Id, string Status, int? Count, int? Bytes, string? Note);

public sealed class GameDataRebuilder(IServiceProvider services, GameBinaryProvider binaries) {
    private static readonly string[] Unbuildable = ["boosts", "habs", "artifacts"];

    public async Task<(IReadOnlyList<RebuildDocResult> Results, string? BinaryNote)> RebuildAsync(CancellationToken ct) {
        var results = new List<RebuildDocResult>();

        (bool ok, byte[]? bin, string version, string? diag) = await binaries.GetBinaryWithVersionAsync(null, ct);
        if (!ok || bin is null) {
            foreach (string id in (string[])["boost-catalog", "missions", "eggs", "vehicles", "dimensions", "research"])
                results.Add(new RebuildDocResult(id, "skipped", null, null, diag ?? "no binary available"));
        } else {
            var syms = MachoSymbols.Read(bin);
            var sections = MachoSections.Read(bin);

            await LandAsync(results, "boost-catalog", () => {
                string configJson = DataCatalog.FixtureText(services, "ei/get_config") ?? "{}";
                var built = BoostCatalogBuilder.Build(bin, configJson, version);
                string? note = built.MissingCosts.Count > 0
                    ? $"{built.MissingCosts.Count} ids without costs: {string.Join(", ", built.MissingCosts)}"
                    : null;
                return (BoostCatalogBuilder.Serialize(built.File), built.File.Boosts.Count, note);
            }, ct);

            await LandAsync(results, "missions", () => {
                var r = MissionCatalogExtractor.ExtractWith(bin, syms, sections);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildMissions(r.Entries, version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "goal-less"));
            }, ct);

            await LandAsync(results, "eggs", () => {
                var r = EggCatalogExtractor.ReadWith(bin, syms, sections);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildEggs(r.Entries, version);
                return (doc.Json, doc.Count, null);
            }, ct);

            await LandAsync(results, "vehicles", () => {
                var r = VehicleCatalogExtractor.ReadWith(bin, syms, sections);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildVehicles(r.Entries, version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "nameless"));
            }, ct);

            await LandAsync(results, "dimensions", () => {
                var r = DimensionCatalogExtractor.ExtractWith(bin, syms, sections);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildDimensions(r.Ids, version);
                return (doc.Json, doc.Count, null);
            }, ct);

            await LandAsync(results, "research", () => {
                var r = ResearchCatalogExtractor.ExtractWith(bin, syms, sections);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildResearch(r.Entries, version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "undecoded"));
            }, ct);
        }

        await LandAsync(results, "colleggtibles", () => {
            var live = LiveColleggtibleSource.Derive(services, DataCatalog.PeriodicalsRoute)
                       ?? throw new InvalidOperationException("no captured get_periodicals to derive from");
            return (live.Json, live.Extract.Eggs.Count, null);
        }, ct);

        foreach (string id in Unbuildable) {
            results.Add(new RebuildDocResult(id, "unbuildable", null, null,
                "no extraction pipeline yet; needs a dedicated extraction session"));
        }

        return (results, ok ? $"binary {version}" : diag);
    }

    private static string? SkipNote(IReadOnlyList<string> skipped, string kind) =>
        skipped.Count == 0 ? null : $"{skipped.Count} {kind} rows dropped: {string.Join(", ", skipped)}";

    private async Task LandAsync(List<RebuildDocResult> results, string id,
        Func<(string Json, int Count, string? Note)> build, CancellationToken ct) {
        string json;
        int count;
        string? note;
        try {
            (json, count, note) = build();
            GameDataProvider.Validate(id, json);
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(id, "failed", null, null, ex.Message));
            return;
        }

        try {
            await UpsertAsync(id, json, ct);
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(id, "failed", count, null, "db write failed: " + ex.Message));
            return;
        }

        results.Add(new RebuildDocResult(id, "built", count, Encoding.UTF8.GetByteCount(json), note));
    }

    private async Task UpsertAsync(string id, string json, CancellationToken ct) {
        var db = services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext
                 ?? throw new InvalidOperationException("no database configured");
        var now = DateTimeOffset.UtcNow;
        var row = await db.GameDataDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) {
            db.GameDataDocuments.Add(new GameDataDocument { Id = id, Json = json, UpdatedAt = now });
        } else {
            row.Json = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
