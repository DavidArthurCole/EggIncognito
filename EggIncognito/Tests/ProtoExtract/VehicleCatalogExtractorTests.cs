using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class VehicleCatalogExtractorTests {
    private static readonly double[] ExpectedLengths = [
        2.1, 2.1, 2.1, 3.4, 4.3, 6.5, 9.6, 6.9, 9.4, 9.5, 7.0, 7.0
    ];

    [Fact]
    public void Read_DecodesTwelveNamedVehicles() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = VehicleCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(ExpectedLengths.Length, r.Entries.Count);
        Assert.Equal("TRIKE", r.Entries[0].Name);
        Assert.Equal("HYPERLOOP TRAIN", r.Entries[11].Name);
    }

    [Fact]
    public void Read_DecodesLengthTable() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = VehicleCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(ExpectedLengths.Length, r.Entries.Count);
        for (int i = 0; i < ExpectedLengths.Length; i++) Assert.Equal(ExpectedLengths[i], r.Entries[i].Length, 6);
    }
}
