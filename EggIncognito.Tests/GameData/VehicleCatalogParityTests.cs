using EggIncognito.GameData;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests.GameData;

public class VehicleCatalogParityTests {
    [Fact]
    public void Committed_catalog_loads_with_expected_shape() {
        var cat = VehicleCatalog.Load();

        Assert.Equal(12, cat.Vehicles.Count);
        Assert.Equal("TRIKE", cat.Find(0)!.Name);
        Assert.Equal(5000, cat.Find(0)!.Capacity);
        Assert.Equal("10 FOOT", cat.Find(3)!.Name);
        Assert.Equal("QUANTUM TRANSPORTER", cat.Find(10)!.Name);
        Assert.Equal(50000000, cat.Find(11)!.Capacity);
        Assert.Equal("binary", cat.Provenance["identity"].Origin);
        Assert.Equal("vehicledata", cat.Provenance["capacity"].Locator);
        Assert.Equal("decoded", cat.Provenance["capacity"].Method);
    }

    [Fact]
    public void Committed_catalog_matches_extractor_output() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var extracted = VehicleCatalogExtractor.Read(bin);
        Assert.True(extracted.Ok, extracted.Diagnostics);

        var committed = VehicleCatalog.Load();
        Assert.Equal(extracted.Entries.Count, committed.Vehicles.Count);
        foreach (var e in extracted.Entries) {
            var c = committed.Find(e.Index);
            Assert.NotNull(c);
            Assert.Equal(e.Name, c!.Name);
            Assert.Equal(e.Capacity, c.Capacity);
        }
    }
}
