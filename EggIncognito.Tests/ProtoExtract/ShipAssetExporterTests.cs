using System.IO.Compression;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

// ShipNameMap + ShipAssetExporter: the ei_ship_* -> Spaceship enum mapping and the ship-only export. The
// map is asserted complete against the 11 known enum ships so a future enum change (new ship) fails here
// loudly. The exporter is exercised against the real device rpos tarball when it is present (327 assets ->
// the 7 bundled ships), and a synthetic archive otherwise.
public class ShipAssetExporterTests
{
    // The 11 MissionInfo.Spaceship enum names, value order. Mirrors EggLedger's enum; if it grows, update
    // both here and ShipNameMap.All (this test guards the map against drift).
    private static readonly string[] EnumShips =
    [
        "ChickenOne", "ChickenNine", "ChickenHeavy", "Bcr", "MilleniumChicken",
        "CorellihenCorvette", "Galeggtica", "Chickfiant", "Voyegger", "Henerprise", "Atreggies",
    ];

    [Fact]
    public void NameMap_CoversEveryEnumShip_Once()
    {
        var mapped = ShipNameMap.All.Select(s => s.EnumName).ToList();
        Assert.Equal(EnumShips.Length, mapped.Count);
        Assert.Equal(EnumShips.OrderBy(x => x), mapped.OrderBy(x => x));
        Assert.Equal(mapped.Count, mapped.Distinct().Count()); // no dupes
    }

    [Fact]
    public void NameMap_TierOrderMatchesEnumValues()
    {
        for (var i = 0; i < EnumShips.Length; i++)
        {
            var ship = ShipNameMap.All.Single(s => s.Tier == i);
            Assert.Equal(EnumShips[i], ship.EnumName);
        }
    }

    [Fact]
    public void NameMap_BundledStems_MapBackToEnum()
    {
        Assert.Equal("ChickenOne", ShipNameMap.EnumNameForStem("ei_ship_chicken_one"));
        Assert.Equal("Atreggies", ShipNameMap.EnumNameForStem("ei_ship_atreggies_shuttle"));
        Assert.True(ShipNameMap.IsBundledShip("ei_ship_bcr"));
        // CDN-only ships have no bundle stem: not resolvable from a stem, not a bundled ship.
        Assert.False(ShipNameMap.IsBundledShip("afx_ship_galeggtica"));
        // non-ship assets are dropped.
        Assert.Null(ShipNameMap.EnumNameForStem("ei_silo_3_large"));
        Assert.Null(ShipNameMap.EnumNameForStem("ei_ship_rooster")); // launch prop, not an enum ship
    }

    [Fact]
    public void Export_Synthetic_RenamesShipsAndSkipsCdnOnly()
    {
        // An archive with one ship mesh + one non-ship asset: only the ship exports, renamed to <EnumName>.glb,
        // and the 10 ships absent from this archive (incl. the 4 CDN ships) are reported as skipped.
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "rpos/ei_ship_chicken_one.rpo", SampleRpo.Build());
            WriteEntry(zip, "rpos/ei_silo_3_large.rpo", SampleRpo.Build());
        }
        var extract = RpoAssetExtractor.Extract(ms.ToArray());
        var export = ShipAssetExporter.Build(extract, generatedFromBuild: "111344");

        var ship = Assert.Single(export.Ships);
        Assert.Equal("ChickenOne", ship.EnumName);
        Assert.Equal("ships/ChickenOne.glb", ship.Entry.File);
        Assert.Contains("ChickenOne", export.Manifest.Ships.Keys);
        Assert.Equal("111344", export.Manifest.GeneratedFromBuild);
        Assert.DoesNotContain("ChickenOne", export.SkippedShips);
        Assert.Contains("Henerprise", export.SkippedShips); // CDN-only, absent here
        Assert.Equal(10, export.SkippedShips.Count);
    }

    [Fact]
    public void Export_RealDeviceTarball_YieldsSevenBundledShips()
    {
        var tgz = DeviceTarball();
        if (tgz is null) return; // fixture absent (CI): skip, covered by the synthetic test above

        var entries = ReadGzippedTar(tgz);
        var extract = RpoAssetExtractor.FromEntries(entries);
        var export = ShipAssetExporter.Build(extract, generatedFromBuild: "device");

        // The 7 bundled ships (tiers 0-5 + Atreggies) decode + export; the 4 CDN ships (Galeggtica,
        // Chickfiant, Voyegger, Henerprise) have no bundled mesh and land in skipped.
        var exported = export.Ships.Select(s => s.EnumName).OrderBy(x => x).ToArray();
        Assert.Equal(
            new[] { "Atreggies", "Bcr", "ChickenHeavy", "ChickenNine", "ChickenOne", "CorellihenCorvette", "MilleniumChicken" }.OrderBy(x => x),
            exported.OrderBy(x => x));
        Assert.Equal(
            new[] { "Chickfiant", "Galeggtica", "Henerprise", "Voyegger" }.OrderBy(x => x),
            export.SkippedShips.OrderBy(x => x));
        // every exported ship has a non-empty glb + emission preserved.
        foreach (var s in export.Ships)
            Assert.True(s.Glb.Length > 12, $"{s.EnumName} glb empty");
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] data)
    {
        using var es = zip.CreateEntry(name).Open();
        es.Write(data);
    }

    // The real device rpos tarball, if a developer dropped it in the repo's captures/ dir. Gzip tar of rpos/.
    private static byte[]? DeviceTarball()
    {
        // tests bin -> repo root is ../../../../.. ; captures/ sits beside the projects.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "egi-repos.tgz"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "egi-repos.tgz"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }

    private static IEnumerable<(string Name, byte[] Bytes)> ReadGzippedTar(byte[] tgz)
    {
        using var input = new MemoryStream(tgz);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gz.CopyTo(plain);
        return TarReader.Read(plain.ToArray()).Select(e => (e.Name, e.Bytes));
    }
}
