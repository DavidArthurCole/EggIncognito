using EggIncognito.ExtractMcp;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ExtractOpsTests {
    private const string BoostIdRegex = "^(?!bd)[a-z][a-z0-9_]{3,}$";

    [Fact]
    public void ReadCatalog_boostmanager_returns_33_entries_with_names() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var ctx = new BinaryContext(bin, "test");
        var entries = ExtractOps.ReadCatalog(ctx, BoostCatalogExtractor.InitSymbol, BoostIdRegex);

        Assert.Equal(33, entries.Count);
        Assert.Equal("MONEY PRINTER", entries.Single(e => e.Id == "money_printer").Name);
        Assert.Equal("LEGENDARY SOUL BEACON", entries.Single(e => e.Id == "soul_beacon_orange").Name);
    }

    [Fact]
    public void ListInits_contains_boostmanager() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var ctx = new BinaryContext(bin, "test");
        var inits = ExtractOps.ListInits(ctx, null);

        Assert.Contains(inits, s => s.Name.Contains("boostmanager", StringComparison.Ordinal));
    }

    [Fact]
    public void BinaryInfo_reports_large_symbol_count() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var ctx = new BinaryContext(bin, "test");
        var info = ExtractOps.BinaryInfo(ctx, []);

        Assert.True(info.SymbolCount > 50000, $"symbol count was {info.SymbolCount}");
    }
}
