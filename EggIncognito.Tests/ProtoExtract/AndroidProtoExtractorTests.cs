using EggIncognito.Services.ProtoExtract;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class AndroidProtoExtractorTests {
    [Fact]
    public void Extract_EmptyBytes_NotOk() {
        var r = AndroidProtoExtractor.Extract([]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Extract_BareSoWithDescriptor_CarvesProto() {


        var fdp = new FileDescriptorProto { Name = "ei.proto", Package = "ei" };
        var blob = fdp.ToByteArray();
        var buf = new byte[64 + blob.Length];
        System.Array.Copy(blob, 0, buf, 32, blob.Length);
        var r = AndroidProtoExtractor.Extract(buf);
        Assert.True(r.Ok);
        Assert.Contains("package ei;", r.Proto);
    }

    [Fact]
    public void ExtractProtoText_NoDescriptor_Throws() => Assert.Throws<System.InvalidOperationException>(() => AndroidProtoExtractor.ExtractProtoText([1, 2, 3, 4]));
}
