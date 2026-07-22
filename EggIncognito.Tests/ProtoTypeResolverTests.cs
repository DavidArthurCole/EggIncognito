using EggIncognito.Services;

namespace EggIncognito.Tests;

public class ProtoTypeResolverTests {
    [Fact]
    public void Resolves_KnownType()
        => Assert.Equal(typeof(Ei.PeriodicalsResponse), ProtoTypeResolver.Resolve("PeriodicalsResponse"));

    [Fact]
    public void Unknown_ReturnsNull()
        => Assert.Null(ProtoTypeResolver.Resolve("NotARealType"));

    [Fact]
    public void NewInstance_CreatesMessage() {
        var t = ProtoTypeResolver.Resolve("PeriodicalsResponse")!;
        Assert.IsType<Google.Protobuf.IMessage>(ProtoTypeResolver.NewInstance(t), exactMatch: false);
    }
}
