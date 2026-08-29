using System.Buffers.Binary;
using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class IosRposTarballFixtureTests {
    private static string? FindFixture() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "captures", "egi-repos.tgz");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static byte[] Gunzip(byte[] gz) {
        using var input = new MemoryStream(gz, false);
        using var dec = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        dec.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void RealDeviceTarball_ParsesAndDecodes() {
        string? fixture = FindFixture();
        if (fixture is null) return;

        byte[] plainTar = Gunzip(File.ReadAllBytes(fixture));

        var entries = TarReader.Read(plainTar);
        Assert.Equal(327, entries.Count);
        Assert.DoesNotContain(entries, e => e.Name.EndsWith('/'));
        Assert.All(entries, e => Assert.True(e.Bytes.Length >= 4 &&
                                             e.Bytes[0] == (byte)'R' && e.Bytes[1] == (byte)'P' &&
                                             e.Bytes[2] == (byte)'O' && e.Bytes[3] == (byte)'1',
            $"{e.Name} is not RPO1"));

        var result = RpoAssetExtractor.FromEntries(entries.Select(e => (e.Name, e.Bytes)));
        Assert.True(result.Ok, result.Diagnostics);

        var ships = result.Assets.Where(a => a.Key.StartsWith("ei_ship_", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(9, ships.Count);
        foreach (var s in ships) {
            Assert.True(s.Decode.Ok, $"{s.Key}: {s.Decode.Diagnostics}");
            Assert.NotNull(s.Decode.Glb);
            Assert.True(s.Decode.Glb!.Length >= 12, $"{s.Key} glb too short");
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(s.Decode.Glb));
            Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(s.Decode.Glb.AsSpan(4)));
        }
    }
}
