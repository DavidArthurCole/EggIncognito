using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class KnownCallModelsTests {
    [Fact]
    public void Sinf_MapsToSinUnary() {
        var u = Assert.IsType<Unary>(KnownCallModels.Resolve("_sinf", [new Input("t")]));
        Assert.Equal(UnOp.Sin, u.Op);
        Assert.Equal(new Input("t"), u.X);
    }

    [Fact]
    public void Cosf_MapsToCosUnary() => Assert.Equal(UnOp.Cos,
        Assert.IsType<Unary>(KnownCallModels.Resolve("_cosf", [new Input("t")])).Op);

    [Fact]
    public void AddParticle_IsSink_CapturesFirstArg() {
        var transform = new Input("placement");
        var o = Assert.IsType<Opaque>(KnownCallModels.Resolve(
            "__ZN19ParticleBatchedMesh11addParticleEN5Eigen9TransformIfLi3ELi2ELi0EEEf", [transform, new ConstExpr(1)]));
        Assert.Equal("@sink", o.Call);
        Assert.Equal(transform, o.Args[0]);
    }

    [Fact]
    public void Unknown_ReturnsNull() => Assert.Null(KnownCallModels.Resolve("__ZN3Foo3barEv", [new ConstExpr(1)]));
}
