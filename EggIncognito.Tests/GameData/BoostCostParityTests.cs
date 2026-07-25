using EggIncognito.GameData;
using EggIncognito.Services;

namespace EggIncognito.Tests.GameData;

public sealed class BoostCostParityTests {
    [Fact]
    public void GameDataBoostCosts_MatchGetConfigCapture() {
        string? path = FindFixture();
        if (path is null) return;

        var costs = BoostCostExtractor.FromConfigJson(File.ReadAllText(path));
        var provider = GameDataProvider.CreateDefault();

        int matched = 0;
        foreach (var e in provider.All("boost")) {
            if (!costs.TryGetValue(e.Id, out var cost)) continue;
            matched++;

            Assert.True(
                e.MetaInt("price") == cost.Price,
                $"boost '{e.Id}' price mismatch: gamedata {e.MetaInt("price")} vs capture {cost.Price}");
            Assert.True(
                e.MetaInt("tokenPrice") == cost.TokenPrice,
                $"boost '{e.Id}' tokenPrice mismatch: gamedata {e.MetaInt("tokenPrice")} vs capture {cost.TokenPrice}");
            Assert.True(
                (int)e.MetaDouble("seRequired") == (int)cost.SeRequired,
                $"boost '{e.Id}' seRequired mismatch: gamedata {(int)e.MetaDouble("seRequired")} vs capture {(int)cost.SeRequired}");
        }

        Assert.True(matched > 0, "no boost ids overlapped between GameData and the get_config capture");
    }

    private static string? FindFixture() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "EggIncognito", "Endpoints", "default", "ei",
                "get_config.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
