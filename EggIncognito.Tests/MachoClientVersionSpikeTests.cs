using EggIncognito.Services.ProtoExtract;
using Xunit.Abstractions;

namespace EggIncognito.Tests;
public class MachoClientVersionSpikeTests(ITestOutputHelper output)
{
    private static readonly string Root =
        Environment.GetEnvironmentVariable("EGGINC_IOS_HISTORICAL_ROOT") ?? @"C:\Users\david\egginc-ios-extract\historical";
    private static readonly string V1287 = Path.Combine(Root, "Egg_INC_1.28.7", "Payload", "egginc.app", "egginc");
    private static readonly string V1293 = Path.Combine(Root, "EGG_INC_Hack_1.29.3", "Payload", "egginc.app", "egginc");

    [Fact]
    public void InlineHeuristic_ReflectsPrev_NotClientVersion()
    {
        if (!File.Exists(V1287)) { output.WriteLine($"SKIP: {V1287} absent"); return; }

        var oldBuild = MachoClientVersionReader.Read(File.ReadAllBytes(V1287), previousClientVersion: 71);
        output.WriteLine($"1.28.7 read(prev=71) = {oldBuild.ClientVersion?.ToString() ?? "null"} (real ~30s)");
        Assert.Equal(72, oldBuild.ClientVersion);
    }

    [Fact]
    public void CandidateSets_AreIdenticalAcrossDifferentVersions_NoSignal()
    {
        if (!File.Exists(V1287) || !File.Exists(V1293)) { output.WriteLine("SKIP: binaries absent"); return; }

        var a = MachoClientVersionReader.Read(File.ReadAllBytes(V1287), 71).Candidates;
        var b = MachoClientVersionReader.Read(File.ReadAllBytes(V1293), 71).Candidates;
        output.WriteLine($"1.28.7 candidates: [{string.Join(",", a)}]");
        output.WriteLine($"1.29.3 candidates: [{string.Join(",", b)}]");

        Assert.Equal(a, b);
    }
}
