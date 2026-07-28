using System.Globalization;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class ParticleCaptureModelTests {
    private static string Line(int t, string mesh, float x, float y, float z, float s) {
        float[] m = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, x, y, z];
        string xs = string.Join(",", m.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        string sl = s.ToString(CultureInfo.InvariantCulture);
        return $"{{\"t\":{t},\"mesh\":\"{mesh}\",\"x\":[{xs}],\"s\":{sl}}}";
    }

    [Fact]
    public void Parse_Empty_NotOk() {
        var m = ParticleCaptureModel.Parse("");
        Assert.False(m.Ok);
        Assert.Equal(0, m.TotalSamples);
    }

    [Fact]
    public void Parse_ClustersByMesh_DominantIsBiggest() {
        string log = string.Join("\n",
            Line(0, "0xA", 1, 2, 3, 0.5f),
            Line(1, "0xA", 1, 2, 3, 0.5f),
            Line(2, "0xA", 1, 2, 3, 0.5f),
            Line(3, "0xB", 9, 9, 9, 1f));
        var m = ParticleCaptureModel.Parse(log);

        Assert.True(m.Ok);
        Assert.Equal(4, m.TotalSamples);
        Assert.Equal(2, m.Clusters.Count);
        Assert.Equal("0xA", m.Dominant!.Value.Mesh);
        Assert.Equal(3, m.Dominant!.Value.Count);
    }

    [Fact]
    public void Summarize_ReadsTranslationFromFourthColumn() {
        string log = Line(0, "0xA", 5, 6, 7, 0.25f);
        var m = ParticleCaptureModel.Parse(log);
        var c = m.Dominant!.Value;
        Assert.Equal(5f, c.Centroid[0], 3);
        Assert.Equal(6f, c.Centroid[1], 3);
        Assert.Equal(7f, c.Centroid[2], 3);
        Assert.Equal(0.25f, c.MeanSize, 3);
    }

    [Fact]
    public void Summarize_RadiusIsMeanHorizontalDistanceFromCentroid() {
        string log = string.Join("\n",
            Line(0, "0xR", 1, 0, 0, 1f),
            Line(1, "0xR", -1, 0, 0, 1f),
            Line(2, "0xR", 0, 0, 1, 1f),
            Line(3, "0xR", 0, 0, -1, 1f));
        var c = ParticleCaptureModel.Parse(log).Dominant!.Value;
        Assert.Equal(1f, c.Radius, 2);
        Assert.Equal(0f, c.BobSpan[1], 3);
        Assert.Equal(2f, c.BobSpan[0], 3);
    }

    [Fact]
    public void Parse_SkipsNaNAndMalformedRecords() {
        const string nan = "{\"t\":0,\"mesh\":\"0xA\",\"x\":[1,0,0,0,1,0,0,0,1,0,0,NaN],\"s\":1}";
        const string short_ = "{\"t\":1,\"mesh\":\"0xA\",\"x\":[1,2,3],\"s\":1}";
        const string junk = "not json";
        string good = Line(2, "0xA", 1, 1, 1, 1f);
        var m = ParticleCaptureModel.Parse(string.Join("\n", nan, short_, junk, good));
        Assert.True(m.Ok);
        Assert.Equal(1, m.TotalSamples);
    }

    [Fact]
    public void Parse_AllBad_NotOk() {
        var m = ParticleCaptureModel.Parse("garbage\nmore garbage");
        Assert.False(m.Ok);
        Assert.Contains("no valid records", m.Diagnostics);
    }
}
