using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class EiAfxDataBuilderTests {
    private static string Read(params string[] parts) {
        string root = CaptureSessionManagerTests.RealContentRoot();
        return File.ReadAllText(Path.Combine([root, "Endpoints", "default", .. parts]));
    }

    private static EiAfxData Build() {
        var icons = DlcArtifactIcons.FromConfigJson(Read("ei", "get_config.json"));
        return EiAfxDataBuilder.BuildFromJson(Read("ei_afx", "config.json"), icons);
    }

    [Fact]
    public void Build_ProducesFamilies_FromRealCapture() {
        var data = Build();

        Assert.NotEmpty(data.ArtifactFamilies);
        Assert.All(data.ArtifactFamilies, f => {
            Assert.False(string.IsNullOrEmpty(f.Id));
            Assert.False(string.IsNullOrEmpty(f.SpecName));
            Assert.NotEmpty(f.Tiers);
            Assert.Equal(f.AfxId, f.ChildAfxIds.Single());
        });
    }

    [Fact]
    public void Build_ClassifiesTypesAndKebabIds() {
        var data = Build();

        var gusset = data.ArtifactFamilies.FirstOrDefault(f => f.AfxId == 8);
        Assert.NotNull(gusset);
        Assert.Equal("ornate-gusset", gusset.Id);
        Assert.Equal("ORNATE_GUSSET", gusset.SpecName);
        Assert.Equal(0, gusset.AfxType);
        Assert.Equal("Artifact", gusset.Type);

        Assert.All(data.ArtifactFamilies.Where(f => f.Id.EndsWith("-stone")), f => Assert.Equal(1, f.AfxType));
        Assert.All(data.ArtifactFamilies.Where(f => f.Id.EndsWith("-fragment")), f => Assert.Equal(3, f.AfxType));
    }

    [Fact]
    public void Build_NoAuthoredDisplayStrings() {
        var data = Build();

        Assert.All(data.ArtifactFamilies.SelectMany(f => f.Tiers), t => {
            Assert.Equal(t.PossibleAfxRarities.Count, t.BaseCraftingPrices.Count);
            Assert.Equal(t.TierNumber, t.AfxLevel + 1);
            if (t.IconFilename is not null)
                Assert.StartsWith("afx_", t.IconFilename);
        });
    }

    [Fact]
    public void Icons_ComeFromRealDlcCatalog() {
        var icons = DlcArtifactIcons.FromConfigJson(Read("ei", "get_config.json"));
        Assert.NotEmpty(icons);

        var data = Build();
        var withIcons = data.ArtifactFamilies.SelectMany(f => f.Tiers).Where(t => t.IconFilename is not null).ToList();
        Assert.NotEmpty(withIcons);
        Assert.All(withIcons, t => {
            Assert.StartsWith("afx_", t.IconFilename!);
            Assert.Contains(t.IconFilename!, icons.Values);
        });
    }
}
