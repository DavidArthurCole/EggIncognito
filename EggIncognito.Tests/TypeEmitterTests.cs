using EggIncognito.Build;

namespace EggIncognito.Tests.TypeEmitterFixtures.A
{
    public sealed record Dup(int X);
}

namespace EggIncognito.Tests.TypeEmitterFixtures.B
{
    public sealed record Dup(int Y);
}

namespace EggIncognito.Tests
{
    // The emitted TS interface name is the CLR simple name, so two records sharing a simple name in
    // different namespaces would silently overwrite each other in types.d.ts. The emitter must fail
    // the build (nonzero exit) instead.
    public class TypeEmitterTests
    {
        public sealed record CollisionRoot(TypeEmitterFixtures.A.Dup First, TypeEmitterFixtures.B.Dup Second);
        public sealed record SoloRoot(string Name, int Count);

        [Fact]
        public void Run_FailsOnSimpleNameCollision()
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"egi-types-{Guid.NewGuid():N}.d.ts");
            try
            {
                Assert.Equal(1, TypeEmitter.Run(outPath, [typeof(CollisionRoot)]));
                Assert.False(File.Exists(outPath));
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        public void Run_SucceedsWithoutCollision()
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"egi-types-{Guid.NewGuid():N}.d.ts");
            try
            {
                Assert.Equal(0, TypeEmitter.Run(outPath, [typeof(SoloRoot)]));
                Assert.Contains("export interface SoloRoot", File.ReadAllText(outPath));
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
