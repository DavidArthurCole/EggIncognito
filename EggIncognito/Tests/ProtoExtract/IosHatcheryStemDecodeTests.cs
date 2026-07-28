using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class IosHatcheryStemDecodeTests {
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


    private static Dictionary<string, byte[]> StemMap(byte[] tar) {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in TarReader.Read(tar)) {
            if (bytes.Length == 0) continue;
            if (!name.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            int slash = name.LastIndexOfAny(['/', '\\']);
            string bn = slash >= 0 ? name[(slash + 1)..] : name;
            int dot = bn.LastIndexOf('.');
            map[dot > 0 ? bn[..dot] : bn] = bytes;
        }

        return map;
    }

    [Fact]
    public void RealTarball_DecodesHatcheryStemsToBounds() {
        string? fixture = FindFixture();
        if (fixture is null) return;

        byte[] tar = Gunzip(File.ReadAllBytes(fixture));
        var map = StemMap(tar);

        var hatchery = map.Keys.Where(k => k.StartsWith("ei_hatchery_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(hatchery);

        foreach (string stem in hatchery) {
            var decode = RpoMeshDecoder.Decode(map[stem], stem);
            Assert.True(decode.Ok, $"{stem}: {decode.Diagnostics}");
            Assert.NotNull(decode.Bounds);
        }
    }
}
