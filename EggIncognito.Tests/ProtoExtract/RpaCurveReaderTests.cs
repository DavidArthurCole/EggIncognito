using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class RpaCurveReaderTests {

    private static byte[] Build(int nComp, (float t, float c0, float c1, float c2)[] keys) {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RPA1"u8.ToArray());
        w.Write(1);
        w.Write(0);
        w.Write(0);
        w.Write(keys.Length);
        w.Write(nComp);
        foreach (var (t, c0, c1, c2) in keys) { w.Write(t); w.Write(c0); w.Write(c1); w.Write(c2); }
        return ms.ToArray();
    }

    [Fact]
    public void Read_BadMagic_NotOk() {
        Assert.False(RpaCurveReader.Read(new byte[64]).Ok);
        Assert.False(RpaCurveReader.Read([1, 2, 3]).Ok);
    }

    [Fact]
    public void Read_ParsesHeaderAndKeys() {
        var bin = Build(3, [(0f, 1f, 2f, 3f), (0.5f, 4f, 5f, 6f), (1f, 7f, 8f, 9f)]);
        var c = RpaCurveReader.Read(bin);
        Assert.True(c.Ok);
        Assert.Equal(1, c.Tracks);
        Assert.Equal(3, c.Components);
        Assert.Equal(3, c.Keys.Count);
        Assert.Equal(0f, c.Keys[0].Time);
        Assert.Equal(7f, c.Keys[2].C0);
        Assert.Equal(1f, c.Duration);
    }

    [Fact]
    public void Read_Truncated_NotOk() {
        var bin = Build(3, [(0f, 1f, 2f, 3f), (1f, 4f, 5f, 6f)]);
        var trunc = bin[..(bin.Length - 8)];
        Assert.False(RpaCurveReader.Read(trunc).Ok);
    }

    [Fact]
    public void Sample_LinearInterpolatesBetweenKeys_AndClampsEnds() {
        var c = RpaCurveReader.Read(Build(1, [(0f, 0f, 0f, 0f), (1f, 10f, 0f, 0f)]));
        Assert.Equal(0f, c.Sample(-1f), 3);
        Assert.Equal(0f, c.Sample(0f), 3);
        Assert.Equal(5f, c.Sample(0.5f), 3);
        Assert.Equal(10f, c.Sample(1f), 3);
        Assert.Equal(10f, c.Sample(2f), 3);
    }

    [Fact]
    public void Sample_PicksComponent() {
        var c = RpaCurveReader.Read(Build(3, [(0f, 1f, 2f, 3f), (1f, 1f, 2f, 3f)]));
        Assert.Equal(1f, c.Sample(0.5f, 0), 3);
        Assert.Equal(2f, c.Sample(0.5f, 1), 3);
        Assert.Equal(3f, c.Sample(0.5f, 2), 3);
    }
}
