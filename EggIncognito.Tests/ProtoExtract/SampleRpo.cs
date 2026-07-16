using System.Buffers.Binary;

namespace EggIncognito.Tests.ProtoExtract;

public static class SampleRpo
{
    public static readonly int[] Strides = [3, 4, 3];

    public static readonly float[][] Positions =
    [
        [0f, 0f, 0f],
        [1f, 0f, 0f],
        [0f, 2f, 0f],
    ];
    public static readonly float[][] Colors =
    [
        [1f, 0f, 0f, 1f],
        [0f, 1f, 0f, 1f],
        [0f, 0f, 1f, 1f],
    ];
    public static readonly float[][] Normals =
    [
        [0f, 0f, 1f],
        [0f, 0f, 1f],
        [0f, 0f, 1f],
    ];
    public static readonly ushort[] Indices = [0, 1, 2, 2, 1, 0];

    public static byte[] Build()
    {
        var indexCount = Indices.Length;
        using var ms = new MemoryStream();
        var u32 = new byte[4];

        void W32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(u32, v); ms.Write(u32); }
        void WF(float v) { BinaryPrimitives.WriteSingleLittleEndian(u32, v); ms.Write(u32); }

        W32(0x314F5052);
        W32((uint)Positions.Length);
        W32((uint)(indexCount * 2));

        foreach (var s in Strides)
        {
            ms.WriteByte((byte)s);
            ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
            ms.WriteByte(0x06); ms.WriteByte(0x14); ms.WriteByte(0x00); ms.WriteByte(0x00);
        }

        W32((uint)indexCount);

        for (var v = 0; v < Positions.Length; v++)
        {
            foreach (var f in Positions[v]) WF(f);
            foreach (var f in Colors[v]) WF(f);
            foreach (var f in Normals[v]) WF(f);
        }

        foreach (var i in Indices) { BinaryPrimitives.WriteUInt16LittleEndian(u32, i); ms.Write(u32.AsSpan(0, 2)); }
        return ms.ToArray();
    }
}
