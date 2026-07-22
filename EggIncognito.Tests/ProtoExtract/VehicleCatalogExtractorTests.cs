using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class VehicleCatalogExtractorTests {
    [Fact]
    public void Extracts_vehicle_names_and_capacities() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = VehicleCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(12, r.Entries.Count);

        Assert.Equal("TRIKE", r.Entries[0].Name);
        Assert.Equal(5000, r.Entries[0].Capacity);
        Assert.Equal("TRANSIT VAN", r.Entries[1].Name);
        Assert.Equal(15000, r.Entries[1].Capacity);
        Assert.Equal("PICKUP", r.Entries[2].Name);
        Assert.Equal(50000, r.Entries[2].Capacity);
        Assert.Equal("10 FOOT", r.Entries[3].Name);
        Assert.Equal(100000, r.Entries[3].Capacity);
        Assert.Equal("QUANTUM TRANSPORTER", r.Entries[10].Name);
        Assert.Equal("HYPERLOOP TRAIN", r.Entries[11].Name);
        Assert.Equal(50000000, r.Entries[11].Capacity);
        Assert.All(r.Entries, e => Assert.True(e.Capacity > 0));
    }
}
