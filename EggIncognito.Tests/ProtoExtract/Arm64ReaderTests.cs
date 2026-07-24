using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Arm64ReaderTests {
    [Fact]
    public void ParseElem_accepts_known_types() {
        Assert.True(Arm64ConstSectionReader.TryParseElem("f64", out var a) && a == TableElemType.F64);
        Assert.True(Arm64ConstSectionReader.TryParseElem("F32", out var b) && b == TableElemType.F32);
        Assert.True(Arm64ConstSectionReader.TryParseElem("i32", out var c) && c == TableElemType.I32);
        Assert.False(Arm64ConstSectionReader.TryParseElem("bogus", out _));
    }
}
