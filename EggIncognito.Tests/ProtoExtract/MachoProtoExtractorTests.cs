using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests.ProtoExtract;


public class MachoProtoExtractorTests
{
    [Fact]
    public void CarveAll_Fixture_FindsThreeDescriptors()
    {
        var carved = MachoProtoExtractor.CarveAll(Fixture());
        Assert.Equal(["ei.proto", "common.proto", "abb.proto"], carved.Select(c => c.Name).ToArray());
        Assert.All(carved, c => Assert.True(c.Bytes.Length > 0));
       
        Assert.True(carved.First(c => c.Name == "ei.proto").Bytes.Length > 50_000);
    }

    [Fact]
    public void EmitProto_Ei_RoundTripsTo152MessagesAnd8Enums()
    {
        var ei = MachoProtoExtractor.CarveAll(Fixture()).First(c => c.Name == "ei.proto");
       
       
        var fdp = FileDescriptorProto.Parser.ParseFrom(ei.Bytes);
        Assert.Equal(152, fdp.MessageType.Count);
        Assert.Equal(8, fdp.EnumType.Count);
        var text = MachoProtoExtractor.EmitProto(ei.Bytes);
        Assert.NotNull(text);
        Assert.Contains("message EggIncFirstContactRequest {", text);
        Assert.Contains("syntax = \"proto2\";", text);
    }

    [Fact]
    public void Extract_Fixture_MergesAndReportsCounts()
    {
        var r = MachoProtoExtractor.Extract(Fixture());
        Assert.True(r.Ok);
        Assert.NotNull(r.Proto);
        Assert.Contains("152 top-level messages", r.Diagnostics);
        Assert.Contains("merged common.proto", r.Diagnostics);
       
        Assert.Contains("enum Platform {", r.Proto);
        Assert.DoesNotContain("aux.Platform", r.Proto);
    }

   
   
    [Fact]
    public void Extract_TopLevelMessages_MatchFrozenSchema()
    {
        var ei = MachoProtoExtractor.CarveAll(Fixture()).First(c => c.Name == "ei.proto");
        var fdp = FileDescriptorProto.Parser.ParseFrom(ei.Bytes);
        var reflection = new ProtoReflection();

        foreach (var msg in fdp.MessageType)
            Assert.True(reflection.FindMessage(msg.Name) is not null, $"frozen ei.proto missing {msg.Name}");

        var fc = fdp.MessageType.First(m => m.Name == "EggIncFirstContactRequest");
        var schema = reflection.Schema("EggIncFirstContactRequest");
        Assert.NotNull(schema);
        foreach (var f in fc.Field)
        {
            var sf = schema!.Fields.FirstOrDefault(x => x.Number == f.Number);
            Assert.True(sf is not null, $"frozen missing field #{f.Number} ({f.Name})");
            Assert.Equal(f.Name, sf!.Name);
        }
    }

    [Fact]
    public void Extract_Garbage_FailsCleanly()
    {
        var r = MachoProtoExtractor.Extract([0xDE, 0xAD, 0xBE, 0xEF, 0, 1, 2, 3]);
        Assert.False(r.Ok);
        Assert.Null(r.Proto);
        Assert.Contains("no ei.proto descriptor", r.Diagnostics);
    }

    [Fact]
    public void Extract_Null_FailsCleanly()
    {
        var r = MachoProtoExtractor.Extract(null!);
        Assert.False(r.Ok);
    }

   
    [Fact]
    public void Extract_RealBinary_WhenProvided()
    {
        var path = Environment.GetEnvironmentVariable("EGGINC_IOS_BINARY");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var r = MachoProtoExtractor.Extract(File.ReadAllBytes(path));
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
    }

    private static byte[] Fixture()
    {
        var dir = Path.GetDirectoryName(SourcePath())!;
        return File.ReadAllBytes(Path.Combine(dir, "egginc-1.35.8-descriptors.bin"));
    }

    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
