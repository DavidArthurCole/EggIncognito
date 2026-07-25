namespace EggIncognito.GameData.Tests;

public sealed class IconBindingTests {
    private static readonly IGameDataProvider Provider = GameDataProvider.CreateDefault();

    [Fact]
    public void Every_boost_row_carries_a_resolvable_icon_asset() {
        foreach (var e in Provider.All(Families.Boost)) {
            Assert.True(e.TryMeta("iconAsset", out _), $"{e.Id} missing iconAsset");
            string icon = e.MetaString("iconAsset");
            Assert.False(string.IsNullOrWhiteSpace(icon), $"{e.Id} blank iconAsset");
            Assert.Equal(ExpectedBoostIcon(e.Id), icon);
        }
    }

    [Fact]
    public void Every_artifact_row_carries_a_resolvable_icon_asset() {
        foreach (var e in Provider.All(Families.Artifact)) {
            Assert.True(e.TryMeta("iconAsset", out _), $"{e.Id} missing iconAsset");
            string icon = e.MetaString("iconAsset");
            Assert.False(string.IsNullOrWhiteSpace(icon), $"{e.Id} blank iconAsset");
            Assert.Equal(ExpectedArtifactIcon(e.Id), icon);
        }
    }

    private static string ExpectedBoostIcon(string boostId) {
        string core = boostId.EndsWith("_v2", StringComparison.Ordinal) ? boostId[..^3] : boostId;
        return "b_icon_" + core;
    }

    private static string ExpectedArtifactIcon(string rowId) {
        string[] parts = rowId.Split(':');
        string name = parts[0].ToLowerInvariant();
        string tier = parts.Length > 1 ? parts[1] : "1";
        return $"afx_{name}_{tier}";
    }
}
