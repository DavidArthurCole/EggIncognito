using System.IO.Compression;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.ProtoExtract;
using SharpGLTF.Schema2;

namespace EggIncognito.Tests.ProtoExtract;

public class GltfAnimatorTests {
    private static byte[] SampleGlb() {
        var decode = RpoMeshDecoder.Decode(SampleRpo.Build(), "TestShip");
        Assert.True(decode.Ok, decode.Diagnostics);
        return decode.Glb!;
    }

    [Fact]
    public void Animate_SpinY_AddsRotationChannel() {
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal("SpinY", r.AnimationName);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = Assert.Single(model.LogicalAnimations);
        Assert.Equal("SpinY", anim.Name);

        Assert.True(anim.Duration is > 5.9f and < 6.1f, $"duration {anim.Duration}");
        Assert.Contains(anim.Channels, c => c.GetRotationSampler() is not null);
    }

    [Fact]
    public void Animate_HoverSpin_AddsRotationAndTranslation() {
        var r = GltfAnimator.Animate(SampleGlb(),
            new GltfAnimator.Options(GltfAnimator.AnimationKind.HoverSpin, 4f, 0.2f));
        Assert.True(r.Ok, r.Diagnostics);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = Assert.Single(model.LogicalAnimations);
        Assert.Contains(anim.Channels, c => c.GetRotationSampler() is not null);
        Assert.Contains(anim.Channels, c => c.GetTranslationSampler() is not null);
    }

    [Fact]
    public void Animate_RoundTripsGeometry() {
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);
        var model = ModelRoot.ParseGLB(r.Glb!);
        var mesh = Assert.Single(model.LogicalMeshes);
        var prim = Assert.Single(mesh.Primitives);
        Assert.NotNull(prim.GetVertexAccessor("POSITION"));
    }

    [Fact]
    public void Animate_OffCenterMesh_PivotsAtCentroid() {
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = model.LogicalAnimations[0];

        var rotNode = anim.Channels.First(c => c.GetRotationSampler() is not null).TargetNode;
        var t = rotNode.LocalTransform.Translation;
        Assert.True(MathF.Abs(t.X - 0.5f) < 1e-4f, $"pivot x {t.X}");
        Assert.True(MathF.Abs(t.Y - 1.0f) < 1e-4f, $"pivot y {t.Y}");

        Assert.Null(rotNode.Mesh);
        var child = rotNode.VisualChildren.Single();
        Assert.NotNull(child.Mesh);
        Assert.True(MathF.Abs(child.LocalTransform.Translation.X + 0.5f) < 1e-4f);
    }

    [Fact]
    public void Animate_BadInput_FailsCleanly() {
        var r = GltfAnimator.Animate([1, 2, 3], GltfAnimator.Options.Spin());
        Assert.False(r.Ok);
        Assert.Null(r.Glb);
    }

    [Fact]
    public void ParseKind_IsCaseInsensitive_DefaultsSpinY() {
        Assert.Equal(GltfAnimator.AnimationKind.HoverSpin, GltfAnimator.ParseKind("hoverspin"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinZ, GltfAnimator.ParseKind("SpinZ"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinY, GltfAnimator.ParseKind("nonsense"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinY, GltfAnimator.ParseKind(null));
    }

    [Fact]
    public void Animate_RealDeviceShip_Spins() {
        byte[]? tgz = DeviceTarball();
        if (tgz is null) return;

        var entries = ReadGzippedTar(tgz);
        var extract = RpoAssetExtractor.FromEntries(entries);
        var ship = extract.Assets.FirstOrDefault(a => a.Key == "ei_ship_bcr" && a.Decode.Ok);
        Assert.True(ship is not null, "ei_ship_bcr should decode from the device tarball");

        var r = GltfAnimator.Animate(ship.Decode.Glb!, GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);
        var model = ModelRoot.ParseGLB(r.Glb!);
        Assert.Single(model.LogicalAnimations);

        Assert.True(model.LogicalMeshes[0].Primitives[0].GetVertexAccessor("POSITION").Count > 100);
    }

    private static byte[]? DeviceTarball() {
        string[] candidates = [
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "egi-repos.tgz"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "egi-repos.tgz")
        ];
        foreach (string c in candidates) {
            string full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }

        return null;
    }

    private static IEnumerable<(string Name, byte[] Bytes)> ReadGzippedTar(byte[] tgz) {
        using var input = new MemoryStream(tgz);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gz.CopyTo(plain);
        return TarReader.Read(plain.ToArray()).Select(e => (e.Name, e.Bytes));
    }
}
