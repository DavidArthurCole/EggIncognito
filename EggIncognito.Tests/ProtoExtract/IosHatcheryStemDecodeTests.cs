using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

// Pins the iOS dump path that DeviceMeshProvider.PullIosRposAsync uses: a device rpos tarball -> (stem -> raw
// bytes) -> RpoMeshDecoder.Decode per stem -> bounds. This is the exact decode loop that lets the hatchery dump
// work on an iOS asset-source device (the listing was wired in 2026-06-30). Uses the same gated real fixture as
// IosRposTarballFixtureTests; soft-skips when absent so fixture-free CI stays green.
public class IosHatcheryStemDecodeTests
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

    // Mirror of PullIosRposAsync's tar -> (stem -> bytes) mapping.
    private static Dictionary<string, byte[]> StemMap(byte[] tar)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, bytes) in TarReader.Read(tar))
        {
            if (bytes.Length == 0) continue;
            if (!name.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase)) continue;
            var slash = name.LastIndexOfAny(['/', '\\']);
            var bn = slash >= 0 ? name[(slash + 1)..] : name;
            var dot = bn.LastIndexOf('.');
            map[dot > 0 ? bn[..dot] : bn] = bytes;
        }
        return map;
    }

    [Fact]
    public void RealTarball_DecodesHatcheryStemsToBounds()
    {
        var fixture = FindFixture();
        if (fixture is null) return; // soft skip

        var tar = Gunzip(File.ReadAllBytes(fixture!));
        var map = StemMap(tar);

        // the dump selects the body + parts per tier; decode them off the in-memory map (no per-piece pull).
        var hatchery = map.Keys.Where(k => k.StartsWith("ei_hatchery_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(hatchery);

        foreach (var stem in hatchery)
        {
            var decode = RpoMeshDecoder.Decode(map[stem], stem);
            Assert.True(decode.Ok, $"{stem}: {decode.Diagnostics}");
            Assert.NotNull(decode.Bounds);
        }
    }
}
