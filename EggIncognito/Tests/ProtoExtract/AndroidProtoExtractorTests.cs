using EggIncognito.Services.ProtoExtract;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests.ProtoExtract;

public class AndroidProtoExtractorTests {
    [Fact]
    public void Extract_BareSoWithDescriptor_CarvesProto() {
        var fdp = new FileDescriptorProto { Name = "ei.proto", Package = "ei" };
        byte[]? blob = fdp.ToByteArray();
        byte[] buf = new byte[64 + blob.Length];
        Array.Copy(blob, 0, buf, 32, blob.Length);
        var r = AndroidProtoExtractor.Extract(buf);
        Assert.True(r.Ok);
        Assert.Contains("package ei;", r.Proto);
    }

    [Fact]
    public void ExtractProtoText_NoDescriptor_Throws() =>
        Assert.Throws<InvalidOperationException>(() => AndroidProtoExtractor.ExtractProtoText([1, 2, 3, 4]));
}
