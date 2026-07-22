using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace EggIncognito.Services.ProtoExtract;


//



//

public static class RpoMeshDecoder {
    private const uint Rpo1Magic = 0x314F5052;
    private const long MaxDecompressedBytes = 200_000_000L;

    public sealed record Vec3(float X, float Y, float Z);
    public sealed record BBox(Vec3 Min, Vec3 Max);


    public sealed record DecodeResult(bool Ok, byte[]? Glb, string Diagnostics,
        int VertexCount, int IndexCount, BBox? Bounds, bool HasEmission, long TrailingBytes = 0);

    private static DecodeResult Fail(string why) => new(false, null, why, 0, 0, null, false);

    public static DecodeResult Decode(byte[] data) => Decode(data, null);


    public static DecodeResult Decode(byte[] data, string? name) {
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

        var bounds = ComputeBounds(rpo, dataStart, vertexCount, floatsPerVertex);

        var hasEmission = strides.Count >= 2 && strides[1] >= 3;
        var glb = BuildGlb(rpo, strides, vertexCount, indexCount, dataStart, vertexBytes, indexBytes, bounds, name);


        var trailing = rpo.Length - (dataStart + vertexBytes + indexBytes);
        return new DecodeResult(true, glb, "ok", vertexCount, indexCount, bounds, hasEmission, trailing);
    }



    private static byte[] Inflate(byte[] data) {
        bool zlib = data.Length >= 2 && data[0] == 0x78 && (data[1] == 0x9C || data[1] == 0x01 || data[1] == 0xDA);
        bool gzip = data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;
        if (!zlib && !gzip) return data;
        try {
            using var input = new MemoryStream(data, writable: false);
            using Stream dec = gzip
                ? new GZipStream(input, CompressionMode.Decompress)
                : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buf = new byte[81920];
            int n; long total = 0;
            while ((n = dec.Read(buf, 0, buf.Length)) > 0) {
                total += n;
                if (total > MaxDecompressedBytes) return data;
                output.Write(buf, 0, n);
            }
            return output.ToArray();
        } catch (InvalidDataException) { return data; }
    }




    private static bool ScanStrides(byte[] rpo, int indexCount, out List<int> strides, out int dataStart) {
        strides = [];
        dataStart = -1;
        var pos = 12;
        while (pos + 4 <= rpo.Length) {
            if (BinaryPrimitives.ReadUInt32LittleEndian(rpo.AsSpan(pos)) == (uint)indexCount) {
                dataStart = pos + 4;
                return true;
            }
            if (pos + 8 <= rpo.Length
                && rpo[pos + 4] == 0x06 && rpo[pos + 5] == 0x14 && rpo[pos + 6] == 0x00 && rpo[pos + 7] == 0x00) {
                strides.Add(rpo[pos]);
            }
            pos += 4;
        }
        return false;
    }

    private static BBox ComputeBounds(byte[] rpo, int dataStart, int vertexCount, int floatsPerVertex) {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        var strideBytes = floatsPerVertex * 4;
        for (var v = 0; v < vertexCount; v++) {
            var o = dataStart + v * strideBytes;
            var x = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o));
            var y = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o + 4));
            var z = BinaryPrimitives.ReadSingleLittleEndian(rpo.AsSpan(o + 8));
            if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
        }
        return new BBox(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
    }



    private static byte[] BuildGlb(byte[] rpo, List<int> strides, int vertexCount, int indexCount,
        int dataStart, long vertexBytes, long indexBytes, BBox bounds, string? name) {

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


        var vertexView = bufferViews.Count;
        bufferViews.Add(new Dictionary<string, object> {
            ["buffer"] = 0,
            ["byteOffset"] = 0,
            ["byteLength"] = (int)vertexBytes,
            ["byteStride"] = vertexStrideBytes,
            ["target"] = 34962,
        });

        var attrByteOffset = 0;
        for (var i = 0; i < strides.Count; i++) {
            var s = strides[i];
            var type = s switch { 2 => "VEC2", 3 => "VEC3", 4 => "VEC4", _ => "SCALAR" };
            var accessor = new Dictionary<string, object> {
                ["bufferView"] = vertexView,
                ["byteOffset"] = attrByteOffset,
                ["componentType"] = 5126,
                ["count"] = vertexCount,
                ["type"] = type,
            };

            if (i == 0) {
                accessor["min"] = new[] { bounds.Min.X, bounds.Min.Y, bounds.Min.Z };
                accessor["max"] = new[] { bounds.Max.X, bounds.Max.Y, bounds.Max.Z };
            }
            var accessorIndex = accessors.Count;
            accessors.Add(accessor);


            if (i == 0) attributes["POSITION"] = accessorIndex;
            else if (i == 1 && s >= 3) attributes["COLOR_0"] = accessorIndex;
            else if (i == 2 && s == 3) attributes["NORMAL"] = accessorIndex;
            else if (s == 2) attributes["TEXCOORD_0"] = accessorIndex;

            attrByteOffset += s * 4;
        }

        var indexView = bufferViews.Count;
        bufferViews.Add(new Dictionary<string, object> {
            ["buffer"] = 0,
            ["byteOffset"] = (int)vertexBytes,
            ["byteLength"] = (int)indexBytes,
            ["target"] = 34963,
        });
        var indexAccessor = accessors.Count;
        accessors.Add(new Dictionary<string, object> {
            ["bufferView"] = indexView,
            ["componentType"] = 5123,
            ["count"] = indexCount,
            ["type"] = "SCALAR",
        });

        var meshName = string.IsNullOrEmpty(name) ? "mesh" : name;
        var gltf = new Dictionary<string, object> {
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
                            ["mode"] = 4,
                        },
                    },
                },
            },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = bin.Length } },
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(gltf);
        var jsonPad = (4 - (json.Length & 3)) & 3;

        return PackGlb(json, jsonPad, bin);
    }


    private static byte[] PackGlb(byte[] json, int jsonPad, byte[] bin) {
        var jsonChunkLen = json.Length + jsonPad;
        var total = 12 + 8 + jsonChunkLen + 8 + bin.Length;
        var glb = new byte[total];
        var span = glb.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonChunkLen);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A);
        json.CopyTo(span[20..]);
        for (var i = 0; i < jsonPad; i++) span[20 + json.Length + i] = 0x20;

        var binChunkStart = 20 + jsonChunkLen;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binChunkStart..], (uint)bin.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binChunkStart + 4)..], 0x004E4942);
        bin.CopyTo(span[(binChunkStart + 8)..]);

        return glb;
    }
}
