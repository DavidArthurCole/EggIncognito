using EggIncognito.Services;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests.GameData;

public class BoostCatalogGenProbe {
    [Fact]
    public void Generate_boost_catalog_json() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        string? configJson = null;
        foreach (var rel in new[]
        {
            "../../../../EggIncognito/Endpoints/default/ei/get_config.json",
            "../../../../../EggIncognito/Endpoints/default/ei/get_config.json",
            "../../../../Endpoints/default/ei/get_config.json"
        }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) { configJson = File.ReadAllText(full); break; }
        }
        if (configJson is null) return;

        var res = BoostCatalogBuilder.Build(bin, configJson, "egginc-1.35.6");
        var json = BoostCatalogBuilder.Serialize(res.File);

        var outPath = Path.Combine(Path.GetTempPath(), "boost-catalog.json");
        File.WriteAllText(outPath, json);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "boost-catalog-missing.txt"),
            $"count={res.File.Boosts.Count}\nmissingCosts=[{string.Join(", ", res.MissingCosts)}]");
        Assert.Equal(33, res.File.Boosts.Count);
    }
}
