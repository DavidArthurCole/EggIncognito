using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class StructInitReaderTests {
    [Fact]
    public void Extracts_egg_base_values_positionally_from_binary() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(18, r.Entries.Count);

        Assert.Equal(0.25, r.Entries[0].BaseValue, 6);
        Assert.Equal(6.25, r.Entries[1].BaseValue, 6);
        Assert.Equal(150, r.Entries[3].BaseValue, 6);
        Assert.Equal(50000, r.Entries[7].BaseValue, 6);
        Assert.Equal(175000, r.Entries[8].BaseValue, 6);
        Assert.Equal(1e14, r.Entries[16].BaseValue, 6);
    }

    [Fact]
    public void Extracts_egg_names_from_binary_cstrings_and_inline_sso() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);

        Assert.Null(r.Entries[0].Name);
        Assert.Equal("MEDICAL", r.Entries[1].Name);
        Assert.Equal("ROCKET FUEL", r.Entries[2].Name);
        Assert.Equal("SUPER MATERIAL", r.Entries[3].Name);
        Assert.Equal("FUSION", r.Entries[4].Name);
        Assert.Equal("QUANTUM", r.Entries[5].Name);
        Assert.Equal("CRISPR", r.Entries[6].Name);
        Assert.Equal("TACHYON", r.Entries[7].Name);
        Assert.Equal("GRAVITON", r.Entries[8].Name);
        Assert.Equal("DILITHIUM", r.Entries[9].Name);
        Assert.Equal("PRODIGY", r.Entries[10].Name);
        Assert.Equal("TERRAFORM", r.Entries[11].Name);
        Assert.Equal("ANTIMATTER", r.Entries[12].Name);
        Assert.Equal("DARK MATTER", r.Entries[13].Name);
        Assert.Equal("UNIVERSE", r.Entries[16].Name);
        Assert.Equal("ENLIGHTENMENT", r.Entries[17].Name);

        Assert.Equal(17, r.Entries.Count(e => e.Name is not null));
    }
}
