using EggIncognito.GameData;
using EggIncognito.Services;

namespace EggIncognito.Tests.GameData;

public sealed class ColleggtibleParityTests
{
    [Fact]
    public void CatalogEggSet_MatchesGetPeriodicalsCapture()
    {
        var path = FindFixture();
        if (path is null) return;

        var extract = ColleggtibleExtractor.FromPeriodicalsJson(File.ReadAllText(path));
        var catalog = GameDataProvider.CreateDefault().Colleggtibles;

        var captureIds = extract.Eggs.Select(e => e.Identifier).OrderBy(x => x, StringComparer.Ordinal);
        var catalogIds = catalog.Eggs.Select(e => e.Identifier).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(catalogIds, captureIds);

        foreach (var wire in extract.Eggs)
        {
            var baked = catalog.Find(wire.Identifier);
            Assert.NotNull(baked);
            Assert.True(
                baked!.Dimension == wire.Dimension,
                $"colleggtible '{wire.Identifier}' dimension mismatch: gamedata {baked.Dimension} vs capture {wire.Dimension}");
            Assert.Equal(wire.TierValues, baked.TierValues);
        }
    }

    [Fact]
    public void ContractMapEntries_ArePresentInCatalog()
    {
        var path = FindFixture();
        if (path is null) return;

        var extract = ColleggtibleExtractor.FromPeriodicalsJson(File.ReadAllText(path));
        var catalog = GameDataProvider.CreateDefault().Colleggtibles;

        foreach (var (contractId, eggId) in extract.ContractEggMap)
        {
            Assert.True(
                catalog.ContractEggMap.TryGetValue(contractId, out var baked) && baked == eggId,
                $"contract '{contractId}' -> '{eggId}' missing from baked catalog map");
        }
    }

    [Fact]
    public void DimensionCodes_EqualFrozenProtoEnum()
    {
        foreach (var (name, code) in ColleggtibleCatalog.DimensionCodes)
        {
            var protoValue = (int)Enum.Parse<Ei.GameModifier.Types.GameDimension>(ToPascal(name));
            Assert.True(protoValue == code, $"dimension '{name}' code {code} != proto {protoValue}");
        }
    }

    private static string ToPascal(string screaming)
    {
        var parts = screaming.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private static string? FindFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "EggIncognito", "Endpoints", "default", "ei", "get_periodicals.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
