using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class ProtoTextIndexTests {
    [Fact]
    public void Extracts_MessagesAndEnums() {
        const string proto = "syntax=\"proto2\";\nmessage Foo { }\nenum Bar { A=0; }\nmessage Baz{}";
        var names = ProtoTextIndex.Names(proto);
        Assert.Contains("Foo", names);
        Assert.Contains("Bar", names);
        Assert.Contains("Baz", names);
    }

    [Fact]
    public void Empty_OnGarbage() => Assert.Empty(ProtoTextIndex.Names("not a proto"));
}
