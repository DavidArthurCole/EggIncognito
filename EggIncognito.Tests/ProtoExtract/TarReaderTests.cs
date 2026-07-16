using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;


public class TarReaderTests
{
    [Fact]
    public void Read_TwoFiles_RoundTrips()
    {
        var a = Encoding.UTF8.GetBytes("hello rpo");
        var b = new byte[600];
        for (var i = 0; i < b.Length; i++) b[i] = (byte)(i & 0xFF);

        var tar = BuildTar(("Henerprise.rpo", a), ("rpos/Voyegger.rpoz", b));
        var entries = TarReader.Read(tar);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Henerprise.rpo", entries[0].Name);
        Assert.Equal(a, entries[0].Bytes);
        Assert.Equal("rpos/Voyegger.rpoz", entries[1].Name);
        Assert.Equal(b, entries[1].Bytes);
    }

    [Fact]
    public void Read_Empty_ReturnsNothing() => Assert.Empty(TarReader.Read([]));

    [Fact]
    public void Read_FeedsAssetExtractor()
    {
        var rpo = SampleRpo.Build();
        var tar = BuildTar(("rpos/Atlas.rpo", rpo));
        var entries = TarReader.Read(tar).Select(e => (e.Name, e.Bytes));
        var r = RpoAssetExtractor.FromEntries(entries);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal("Atlas", r.Assets[0].Key);
    }

   
   
    private static byte[] BuildTar(params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        foreach (var (name, data) in files)
        {
            var header = new byte[512];
            var nameBytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, header, Math.Min(nameBytes.Length, 100));
            WriteOctal(header, 100, 8, 0b110_100_100);
            WriteOctal(header, 124, 12, data.Length);
            header[156] = (byte)'0';
            Encoding.ASCII.GetBytes("ustar\0").CopyTo(header, 257);
            for (var i = 148; i < 156; i++) header[i] = (byte)' ';
            var sum = 0; foreach (var x in header) sum += x;
            WriteOctal(header, 148, 7, sum); header[155] = (byte)' ';

            ms.Write(header);
            ms.Write(data);
            var pad = (512 - (data.Length & 511)) & 511;
            ms.Write(new byte[pad]);
        }
        ms.Write(new byte[1024]);
        return ms.ToArray();
    }

    private static void WriteOctal(byte[] buf, int offset, int len, long value)
    {
        var s = Convert.ToString(value, 8).PadLeft(len - 1, '0');
        Encoding.ASCII.GetBytes(s).CopyTo(buf, offset);
        buf[offset + len - 1] = 0;
    }
}
