using System.Buffers.Binary;
using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

// End-to-end check of the iOS 3D-asset pull path against a REAL device tarball, without a live device.
// The fixture captures\egi-repos.tgz is a gzip tar of the on-device rpos/ dir (327 .rpo). The device path
// (IosAssetPuller) emits a PLAIN (uncompressed) `tar -cf`, so we gunzip the fixture to recover the same
// plain tar the puller would scp back, then run it through the exact server-side decode: TarReader.Read ->
// RpoAssetExtractor.FromEntries. Asserts 327 entries parse and the 9 ei_ship_* meshes decode to valid glb.
//
// GATED: skips (does not fail) when the fixture is absent so CI without the capture stays green.
public class IosRposTarballFixtureTests
{
    private static string? FindFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "captures", "egi-repos.tgz");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static byte[] Gunzip(byte[] gz)
    {
        using var input = new MemoryStream(gz, writable: false);
        using var dec = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        dec.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void RealDeviceTarball_ParsesAndDecodes()
    {
        var fixture = FindFixture();
        if (fixture is null) return; // CI-safe soft skip: xunit v2 has no runtime-skip; absent fixture = no-op pass

        var plainTar = Gunzip(File.ReadAllBytes(fixture!)); // device tar is uncompressed; gunzip the .tgz capture

        var entries = TarReader.Read(plainTar);
        // 327 files; the rpos/ directory entry (typeflag '5') must NOT appear.
        Assert.Equal(327, entries.Count);
        Assert.DoesNotContain(entries, e => e.Name.EndsWith("/"));
        Assert.All(entries, e => Assert.True(e.Bytes.Length >= 4 &&
            e.Bytes[0] == (byte)'R' && e.Bytes[1] == (byte)'P' && e.Bytes[2] == (byte)'O' && e.Bytes[3] == (byte)'1',
            $"{e.Name} is not RPO1"));

        var result = RpoAssetExtractor.FromEntries(entries.Select(e => (e.Name, e.Bytes)));
        Assert.True(result.Ok, result.Diagnostics);

        var ships = result.Assets.Where(a => a.Key.StartsWith("ei_ship_", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(9, ships.Count);
        foreach (var s in ships)
        {
            Assert.True(s.Decode.Ok, $"{s.Key}: {s.Decode.Diagnostics}");
            Assert.NotNull(s.Decode.Glb);
            // valid glb = 12-byte header, magic "glTF" (0x46546C67 LE), version 2.
            Assert.True(s.Decode.Glb!.Length >= 12, $"{s.Key} glb too short");
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(s.Decode.Glb));
            Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(s.Decode.Glb.AsSpan(4)));
        }
    }
}
