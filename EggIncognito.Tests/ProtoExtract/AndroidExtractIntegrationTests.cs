using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class AndroidExtractIntegrationTests
{
    private static string? Fixture => Environment.GetEnvironmentVariable("EGI_TEST_ARMSPLIT");

    [Fact]
    public void RealArmSplit_CarvesProto_StructurallyMatchesFrozen()
    {
        var path = Fixture;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var proto = AndroidProtoExtractor.ExtractProtoText(File.ReadAllBytes(path));
        var names = ProtoTextIndex.Names(proto);
       
        Assert.True(names.Count >= 200, $"only {names.Count} messages carved");
        Assert.Contains("BasicRequestInfo", names);
    }

    [Fact]
    public void RealArmSplit_ReadsClientVersion()
    {
        var path = Fixture;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var prev = int.TryParse(Environment.GetEnvironmentVariable("EGI_TEST_PREV_CV"), out var p) ? p : 71;
        var cv = LibegincClientVersion.Read(File.ReadAllBytes(path), prev);
        Assert.NotNull(cv);
        Assert.InRange(cv!.Value, prev, prev + 2);
    }
}
