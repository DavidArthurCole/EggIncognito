using System.IO.Compression;
using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class LibegincClientVersionTests {
    private static uint Movz(int wd, int imm16) => 0x52800000u | ((uint)(imm16 & 0xFFFF) << 5) | (uint)(wd & 0x1F);

    private static uint StrW(int wt, int xn, int imm12) =>
        0xB9000000u | ((uint)(imm12 & 0xFFF) << 10) | ((uint)(xn & 0x1F) << 5) | (uint)(wt & 0x1F);


    private static byte[] SoWithText(byte[] textBytes) {
        byte[] shstrtab = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");
        const int ehdr = 64, shentsize = 64, shstrtabOff = ehdr;
        int textOff = shstrtabOff + shstrtab.Length;
        int shoff = textOff + textBytes.Length;
        const int shnum = 3, shstrndx = 2;
        byte[] buf = new byte[shoff + shnum * shentsize];
        buf[0] = 0x7F;
        buf[1] = (byte)'E';
        buf[2] = (byte)'L';
        buf[3] = (byte)'F';
        buf[4] = 2;
        buf[5] = 1;

        void U16(int p, ushort v) {
            buf[p] = (byte)v;
            buf[p + 1] = (byte)(v >> 8);
        }

        void U32(int p, uint v) {
            for (int i = 0; i < 4; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        void U64(int p, ulong v) {
            for (int i = 0; i < 8; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        U64(0x28, (ulong)shoff);
        U16(0x3A, shentsize);
        U16(0x3C, shnum);
        U16(0x3E, shstrndx);
        Array.Copy(shstrtab, 0, buf, shstrtabOff, shstrtab.Length);
        Array.Copy(textBytes, 0, buf, textOff, textBytes.Length);
        int s1 = shoff + shentsize;
        U32(s1 + 0x00, 1);
        U64(s1 + 0x10, 0x1000);
        U64(s1 + 0x18, (ulong)textOff);
        U64(s1 + 0x20, (ulong)textBytes.Length);
        int s2 = shoff + 2 * shentsize;
        U32(s2 + 0x00, 7);
        U64(s2 + 0x18, shstrtabOff);
        U64(s2 + 0x20, (ulong)shstrtab.Length);
        return buf;
    }

    private static byte[] Text() {
        var w = new List<uint>();
        for (int s = 0; s < 3; s++) {
            w.Add(Movz(0, 72));
            w.Add(StrW(0, 1, 0x11));
            w.Add(0xD503201F);
        }

        byte[] b = new byte[w.Count * 4];
        for (int i = 0; i < w.Count; i++) {
            for (int k = 0; k < 4; k++)
                b[i * 4 + k] = (byte)(w[i] >> (8 * k));
        }

        return b;
    }

    [Fact]
    public void Read_RawSo_FindsClientVersion() => Assert.Equal(72, LibegincClientVersion.Read(SoWithText(Text()), 71));

    [Fact]
    public void Read_NullPrev_ReturnsNull() => Assert.Null(LibegincClientVersion.Read(SoWithText(Text()), null));

    [Fact]
    public void Read_ApkZip_PullsSoEntry() {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            var e = zip.CreateEntry("lib/arm64-v8a/libegginc.so");
            using var es = e.Open();
            byte[] so = SoWithText(Text());
            es.Write(so, 0, so.Length);
        }

        Assert.Equal(72, LibegincClientVersion.Read(ms.ToArray(), 71));
    }
}
