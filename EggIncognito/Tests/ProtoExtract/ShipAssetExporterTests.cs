using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services;

namespace EggIncognito.Tests.ProtoExtract;

public class ShipAssetExporterTests {
    private static readonly string[] EnumShips = [
        "ChickenOne", "ChickenNine", "ChickenHeavy", "Bcr", "MilleniumChicken",
        "CorellihenCorvette", "Galeggtica", "Chickfiant", "Voyegger", "Henerprise", "Atreggies"
    ];

    [Fact]
    public void NameMap_CoversEveryEnumShip_Once() {
        var mapped = ShipNameMap.All.Select(s => s.EnumName).ToList();
        Assert.Equal(EnumShips.Length, mapped.Count);
        Assert.Equal(EnumShips.OrderBy(x => x), mapped.OrderBy(x => x));
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Fact]
    public void NameMap_TierOrderMatchesEnumValues() {
        for (int i = 0; i < EnumShips.Length; i++) {
            var ship = ShipNameMap.All.Single(s => s.Tier == i);
            Assert.Equal(EnumShips[i], ship.EnumName);
        }
    }

    [Fact]
    public void NameMap_BundledStems_MapBackToEnum() {
        Assert.Equal("ChickenOne", ShipNameMap.EnumNameForStem("ei_ship_chicken_one"));
        Assert.Equal("Atreggies", ShipNameMap.EnumNameForStem("ei_ship_atreggies_shuttle"));
        Assert.True(ShipNameMap.IsBundledShip("ei_ship_bcr"));
        Assert.False(ShipNameMap.IsBundledShip("afx_ship_galeggtica"));
        Assert.Null(ShipNameMap.EnumNameForStem("ei_silo_3_large"));
        Assert.Null(ShipNameMap.EnumNameForStem("ei_ship_rooster"));
    }

    [Fact]
    public void Export_Synthetic_RenamesShipsAndSkipsCdnOnly() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            WriteEntry(zip, "rpos/ei_ship_chicken_one.rpo", SampleRpo.Build());
            WriteEntry(zip, "rpos/ei_silo_3_large.rpo", SampleRpo.Build());
        }

        var extract = RpoAssetExtractor.Extract(ms.ToArray());
        var export = ShipAssetExporter.Build(extract, "111344");

        var ship = Assert.Single(export.Ships);
        Assert.Equal("ChickenOne", ship.EnumName);
        Assert.Equal("ships/ChickenOne.glb", ship.Entry.File);
        Assert.Contains("ChickenOne", export.Manifest.Ships.Keys);
        Assert.Equal("111344", export.Manifest.GeneratedFromBuild);
        Assert.DoesNotContain("ChickenOne", export.SkippedShips);
        Assert.Contains("Henerprise", export.SkippedShips);
        Assert.Equal(10, export.SkippedShips.Count);
    }

    [Fact]
    public void Export_RealDeviceTarball_YieldsSevenBundledShips() {
        byte[]? tgz = DeviceTarball();
        if (tgz is null) return;

        var entries = ReadGzippedTar(tgz);
        var extract = RpoAssetExtractor.FromEntries(entries);
        var export = ShipAssetExporter.Build(extract, "device");

        string[] exported = [.. export.Ships.Select(s => s.EnumName).OrderBy(x => x)];
        Assert.Equal(
            new[] {
                "Atreggies", "Bcr", "ChickenHeavy", "ChickenNine", "ChickenOne", "CorellihenCorvette",
                "MilleniumChicken"
            }.OrderBy(x => x),
            exported.OrderBy(x => x));
        Assert.Equal(
            new[] { "Chickfiant", "Galeggtica", "Henerprise", "Voyegger" }.OrderBy(x => x),
            export.SkippedShips.OrderBy(x => x));
        foreach (var s in export.Ships)
            Assert.True(s.Glb.Length > 12, $"{s.EnumName} glb empty");
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] data) {
        using var es = zip.CreateEntry(name).Open();
        es.Write(data);
    }

    private static byte[]? DeviceTarball() {
        string[] candidates = [
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "egi-repos.tgz"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "egi-repos.tgz")
        ];
        foreach (string c in candidates) {
            string full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }

        return null;
    }

    private static IEnumerable<(string Name, byte[] Bytes)> ReadGzippedTar(byte[] tgz) {
        using var input = new MemoryStream(tgz);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gz.CopyTo(plain);
        return TarReader.Read(plain.ToArray()).Select(e => (e.Name, e.Bytes));
    }
}
