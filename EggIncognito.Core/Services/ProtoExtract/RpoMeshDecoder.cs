using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Services.ProtoExtract;

// Decodes Egg Inc's .rpo / .rpoz 3D mesh format to a web-loadable glTF 2.0 binary (.glb). Reimplemented
// in C# from rpotool (https://github.com/tylertms/rpotool, MIT) so the asset pipeline carries no Rust /
// Python / Blender runtime dependency. The meshes remain property of Auxbrain Inc.; this only reformats
// them. Pure + defensive: malformed input yields a failed result, never a throw. Bytes are parsed, never
// executed.
//
// .rpo layout (all little-endian):
//   0..4   magic bytes "RPO1" (0x52 50 4F 31 on disk; 0x314F5052 as a LE u32)
//   4..8   vertex count (u32)
//   8..12  face bytes (u32); index count = face_bytes / 2 (u16 indices)
//   then   a header of 8-byte stride descriptors; each descriptor whose bytes [4..8] == 06 14 00 00
//          contributes one vertex attribute, its component count = descriptor byte [0] (2=Vec2, 3=Vec3,
//          4=Vec4). The header ends at the 4-byte window whose u32 == face_bytes/2.
//   then   interleaved f32 vertex data (sum(strides) floats per vertex)
//   then   u16 indices (face_bytes/2 of them)
//
// rpotool assigns semantics positionally, not from the file: accessor[0]=POSITION always;
// accessor[1]=COLOR_0 when its stride>=3 (this is EI's per-vertex EMISSION, must survive into the .glb);
// accessor[2]=NORMAL when its stride==3. We match that mapping exactly. .rpoz = the same stream wrapped
// in a zlib container (0x78 0x9C); inflate first, then parse as .rpo.
public static class RpoMeshDecoder
{
    private const uint Rpo1Magic = 0x314F5052; // bytes 'R','P','O','1' (0x52 50 4F 31) read as a LE u32
    private const long MaxDecompressedBytes = 200_000_000L; // shell meshes are tiny; guard zip bombs on the public path

    public sealed record Vec3(float X, float Y, float Z);
    public sealed record BBox(Vec3 Min, Vec3 Max);

    // Glb = the assembled .glb bytes. VertexCount / IndexCount + BBox mirror what a consumer needs without
    // re-parsing (the manifest bbox comes straight from here). HasEmission is true when a COLOR_0 attribute
    // survived, so a caller can detect the silent emission-drop regression.
    public sealed record DecodeResult(bool Ok, byte[]? Glb, string Diagnostics,
        int VertexCount, int IndexCount, BBox? Bounds, bool HasEmission);

    private static DecodeResult Fail(string why) => new(false, null, why, 0, 0, null, false);

    public static DecodeResult Decode(byte[] data) => Decode(data, null);

    // name is folded into the glTF node/mesh name when supplied (the ship enum key), purely cosmetic.
    public static DecodeResult Decode(byte[] data, string? name)
    {
        if (data is null || data.Length < 12) return Fail("input too short");

        var rpo = Inflate(data);
        if (rpo.Length < 12) return Fail("decompressed input too short");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(rpo);
        if (magic != Rpo1Magic) return Fail($"bad magic 0x{magic:X8}, expected RPO1");

        var vertexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(rpo.AsSpan(4));
        var faceBytes = (int)BinaryPrimitives.ReadUInt32LittleEndian(rpo.AsSpan(8));
        if (vertexCount <= 0 || faceBytes <= 0 || (faceBytes & 1) != 0) return Fail("invalid vertex/face counts");
        var indexCount = faceBytes / 2;

        if (!ScanStrides(rpo, indexCount, out var strides, out var dataStart))
            return Fail("could not find end of header");
        if (strides.Count == 0) return Fail("no vertex attributes found");

        var floatsPerVertex = 0;
        foreach (var s in strides) floatsPerVertex += s;
        var vertexBytes = (long)vertexCount * floatsPerVertex * 4;
        var indexBytes = (long)indexCount * 2;
        if (dataStart + vertexBytes + indexBytes > rpo.Length)
            return Fail("vertex/index data runs past end of buffer");

        // Bounds: read the first stride (always POSITION per rpotool) to compute the bbox for the manifest.
        var bounds = ComputeBounds(rpo, dataStart, vertexCount, floatsPerVertex);

        var hasEmission = strides.Count >= 2 && strides[1] >= 3;
        var glb = BuildGlb(rpo, strides, vertexCount, indexCount, dataStart, vertexBytes, indexBytes, bounds, name);
        return new DecodeResult(true, glb, "ok", vertexCount, indexCount, bounds, hasEmission);
    }

    // .rpoz wraps the .rpo stream in a zlib container (0x78 0x9C). Inflate it; otherwise return as-is. A
    // gzip-wrapped variant is tolerated too. Caps the output so a crafted stream cannot exhaust memory.
    private static byte[] Inflate(byte[] data)
    {
        bool zlib = data.Length >= 2 && data[0] == 0x78 && (data[1] == 0x9C || data[1] == 0x01 || data[1] == 0xDA);
        bool gzip = data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;
        if (!zlib && !gzip) return data;
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using Stream dec = gzip
                ? new GZipStream(input, CompressionMode.Decompress)
                : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buf = new byte[81920];
            int n; long total = 0;
            while ((n = dec.Read(buf, 0, buf.Length)) > 0)
            {
                total += n;
                if (total > MaxDecompressedBytes) return data; // refuse a bomb; caller fails on magic mismatch
                output.Write(buf, 0, n);
            }
            return output.ToArray();
        }
        catch (InvalidDataException) { return data; }
    }

    // Walks the 8-byte-descriptor header from offset 12. A 4-byte window whose u32 == indexCount marks the
    // end of the header (rpotool's terminator). Along the way, every descriptor whose bytes [4..8] are
    // 06 14 00 00 contributes one attribute with component count = byte[0]. The scan steps 4 bytes at a
    // time exactly as rpotool does (it split_off(4) each iteration). dataStart lands on the first vertex.
    private static bool ScanStrides(byte[] rpo, int indexCount, out List<int> strides, out int dataStart)
    {
        strides = [];
        dataStart = -1;
        var pos = 12;
        while (pos + 4 <= rpo.Length)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(rpo.AsSpan(pos)) == (uint)indexCount)
            {
                dataStart = pos + 4; // skip the terminator window, vertex data follows
                return true;
            }
            if (pos + 8 <= rpo.Length
                && rpo[pos + 4] == 0x06 && rpo[pos + 5] == 0x14 && rpo[pos + 6] == 0x00 && rpo[pos + 7] == 0x00)
            {
                strides.Add(rpo[pos]);
            }
            pos += 4;
        }
        return false;
    }

    private static BBox ComputeBounds(byte[] rpo, int dataStart, int vertexCount, int floatsPerVertex)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        var strideBytes = floatsPerVertex * 4;
        for (var v = 0; v < vertexCount; v++)
        {
            var o = dataStart + v * strideBytes;
            var x = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o));
            var y = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o + 4));
            var z = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o + 8));
            if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
        }
        return new BBox(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
    }

    // Assembles a single-mesh GLB. One interleaved BIN region holds the vertex block followed by the index
    // block. Each attribute is one accessor over the shared interleaved vertex bufferView (byteStride =
    // floatsPerVertex*4, byteOffset = cumulative stride*4), matching rpotool's deinterleaved-accessor view
    // over interleaved data. Semantics assigned positionally per rpotool.
    private static byte[] BuildGlb(byte[] rpo, List<int> strides, int vertexCount, int indexCount,
        int dataStart, long vertexBytes, long indexBytes, BBox bounds, string? name)
    {
        // BIN = vertex block (copied verbatim, already interleaved f32) + index block (u16). 4-byte aligned.
        var binLen = (int)(vertexBytes + indexBytes);
        var pad = (4 - (binLen & 3)) & 3;
        var bin = new byte[binLen + pad];
        Array.Copy(rpo, dataStart, bin, 0, (int)vertexBytes);
        Array.Copy(rpo, dataStart + (int)vertexBytes, bin, (int)vertexBytes, (int)indexBytes);

        var floatsPerVertex = 0;
        foreach (var s in strides) floatsPerVertex += s;
        var vertexStrideBytes = floatsPerVertex * 4;

        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var attributes = new Dictionary<string, int>();

        // One accessor per attribute, all over the same interleaved vertex bufferView.
        var vertexView = bufferViews.Count;
        bufferViews.Add(new Dictionary<string, object>
        {
            ["buffer"] = 0,
            ["byteOffset"] = 0,
            ["byteLength"] = (int)vertexBytes,
            ["byteStride"] = vertexStrideBytes,
            ["target"] = 34962, // ARRAY_BUFFER
        });

        var attrByteOffset = 0;
        for (var i = 0; i < strides.Count; i++)
        {
            var s = strides[i];
            var type = s switch { 2 => "VEC2", 3 => "VEC3", 4 => "VEC4", _ => "SCALAR" };
            var accessor = new Dictionary<string, object>
            {
                ["bufferView"] = vertexView,
                ["byteOffset"] = attrByteOffset,
                ["componentType"] = 5126, // FLOAT
                ["count"] = vertexCount,
                ["type"] = type,
            };
            // POSITION gets min/max (required by the glTF spec for the position accessor).
            if (i == 0)
            {
                accessor["min"] = new[] { bounds.Min.X, bounds.Min.Y, bounds.Min.Z };
                accessor["max"] = new[] { bounds.Max.X, bounds.Max.Y, bounds.Max.Z };
            }
            var accessorIndex = accessors.Count;
            accessors.Add(accessor);

            // rpotool semantics: [0]=POSITION, [1]=COLOR_0 if stride>=3 (EI emission), [2]=NORMAL if stride==3.
            if (i == 0) attributes["POSITION"] = accessorIndex;
            else if (i == 1 && s >= 3) attributes["COLOR_0"] = accessorIndex;
            else if (i == 2 && s == 3) attributes["NORMAL"] = accessorIndex;
            else if (s == 2) attributes["TEXCOORD_0"] = accessorIndex;

            attrByteOffset += s * 4;
        }

        // Index bufferView + accessor.
        var indexView = bufferViews.Count;
        bufferViews.Add(new Dictionary<string, object>
        {
            ["buffer"] = 0,
            ["byteOffset"] = (int)vertexBytes,
            ["byteLength"] = (int)indexBytes,
            ["target"] = 34963, // ELEMENT_ARRAY_BUFFER
        });
        var indexAccessor = accessors.Count;
        accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = indexView,
            ["componentType"] = 5123, // UNSIGNED_SHORT
            ["count"] = indexCount,
            ["type"] = "SCALAR",
        });

        var meshName = string.IsNullOrEmpty(name) ? "mesh" : name;
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "EggIncognito RpoMeshDecoder" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object> { ["nodes"] = new[] { 0 } } },
            ["nodes"] = new[] { new Dictionary<string, object> { ["mesh"] = 0, ["name"] = meshName } },
            ["meshes"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = meshName,
                    ["primitives"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["attributes"] = attributes,
                            ["indices"] = indexAccessor,
                            ["mode"] = 4, // TRIANGLES
                        },
                    },
                },
            },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = bin.Length } },
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(gltf);
        var jsonPad = (4 - (json.Length & 3)) & 3; // JSON chunk padded with spaces to 4 bytes

        return PackGlb(json, jsonPad, bin);
    }

    // GLB container: 12-byte header (magic "glTF", version 2, total length) + JSON chunk + BIN chunk.
    private static byte[] PackGlb(byte[] json, int jsonPad, byte[] bin)
    {
        var jsonChunkLen = json.Length + jsonPad;
        var total = 12 + 8 + jsonChunkLen + 8 + bin.Length;
        var glb = new byte[total];
        var span = glb.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67); // "glTF"
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonChunkLen);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A); // "JSON"
        json.CopyTo(span[20..]);
        for (var i = 0; i < jsonPad; i++) span[20 + json.Length + i] = 0x20; // space padding

        var binChunkStart = 20 + jsonChunkLen;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binChunkStart..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binChunkStart + 4)..], 0x004E4942); // "BIN\0"
        bin.CopyTo(span[(binChunkStart + 8)..]);

        return glb;
    }
}
