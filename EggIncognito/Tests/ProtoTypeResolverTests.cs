using EggIncognito.Core.Services;
using Ei;

namespace EggIncognito.Tests;

public class ProtoTypeResolverTests {
    [Fact]
    public void Resolves_KnownType()
        => Assert.Equal(typeof(PeriodicalsResponse), ProtoTypeResolver.Resolve("PeriodicalsResponse"));

    [Fact]
    public void Unknown_ReturnsNull()
        => Assert.Null(ProtoTypeResolver.Resolve("NotARealType"));
}
