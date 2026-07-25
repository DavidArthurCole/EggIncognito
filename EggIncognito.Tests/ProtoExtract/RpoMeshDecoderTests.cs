using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class RpoMeshDecoderTests {
    private static byte[] BuildRpo() => SampleRpo.Build();

    [Fact]
    public void Decode_SyntheticTriangle_ParsesCountsAndBounds() {
        var r = RpoMeshDecoder.Decode(BuildRpo());
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(3, r.VertexCount);
        Assert.Equal(6, r.IndexCount);
        Assert.NotNull(r.Bounds);
        Assert.Equal(0f, r.Bounds!.Min.X);
        Assert.Equal(0f, r.Bounds.Min.Y);
        Assert.Equal(1f, r.Bounds.Max.X);
        Assert.Equal(2f, r.Bounds.Max.Y);
    }

    [Fact]
    public void Decode_PreservesEmissionAsColor0() {
        var r = RpoMeshDecoder.Decode(BuildRpo());
        Assert.True(r.HasEmission, "emission (COLOR_0) must survive into the glb");

        var attrs = PrimitiveAttributes(r.Glb!);
        Assert.True(attrs.ContainsKey("COLOR_0"), "glb primitive must expose COLOR_0");
        Assert.True(attrs.ContainsKey("POSITION"));
        Assert.True(attrs.ContainsKey("NORMAL"));
    }

    [Fact]
    public void Decode_ProducesValidGlbContainer() {
        byte[] glb = RpoMeshDecoder.Decode(BuildRpo()).Glb!;
        Assert.Equal((uint)0x46546C67, BinaryPrimitives.ReadUInt32LittleEndian(glb));
        Assert.Equal((uint)2, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        Assert.Equal((uint)0x4E4F534A, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)));
        Assert.Equal(0, glb.Length % 4);
    }

    [Fact]
    public void Decode_Rpoz_ZlibWrapped_RoundTrips() {
        byte[] rpoz = ZlibWrap(BuildRpo());
        var r = RpoMeshDecoder.Decode(rpoz);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(3, r.VertexCount);
        Assert.Equal(6, r.IndexCount);
    }

    [Fact]
    public void Decode_BadMagic_FailsCleanly() {
        byte[] junk = new byte[64];
        junk[0] = 0xDE;
        junk[1] = 0xAD;
        var r = RpoMeshDecoder.Decode(junk);
        Assert.False(r.Ok);
        Assert.Null(r.Glb);
    }

    [Fact]
    public void Decode_Empty_FailsCleanly() => Assert.False(RpoMeshDecoder.Decode([]).Ok);

    [Fact]
    public void Extract_ZipWithRpo_DecodesNamedByEntry() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            using var es = zip.CreateEntry("assets/rpos/Henerprise.rpo").Open();
            es.Write(BuildRpo());
        }

        var r = RpoAssetExtractor.Extract(ms.ToArray());
        Assert.True(r.Ok, r.Diagnostics);
        var asset = Assert.Single(r.Assets);
        Assert.Equal("Henerprise", asset.Key);
        Assert.True(asset.Decode.Ok);
    }

    [Fact]
    public void Extract_NoMeshEntries_ReportsNotFound() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            using var es = zip.CreateEntry("AndroidManifest.xml").Open();
            es.Write([1, 2, 3]);
        }

        var r = RpoAssetExtractor.Extract(ms.ToArray());
        Assert.False(r.Ok);
        Assert.Empty(r.Assets);
    }

    private static byte[] ZlibWrap(byte[] data) {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);

        ms.SetLength(0);
        using (var zl = new ZLibStream(ms, CompressionLevel.Optimal, true))
            zl.Write(data);
        return ms.ToArray();
    }


    private static Dictionary<string, JsonElement> PrimitiveAttributes(byte[] glb) {
        int jsonLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        string json = Encoding.UTF8.GetString(glb, 20, jsonLen);
        using var doc = JsonDocument.Parse(json);
        var attrs = doc.RootElement
            .GetProperty("meshes")[0]
            .GetProperty("primitives")[0]
            .GetProperty("attributes");
        var map = new Dictionary<string, JsonElement>();
        foreach (var p in attrs.EnumerateObject()) map[p.Name] = p.Value.Clone();
        return map;
    }
}
