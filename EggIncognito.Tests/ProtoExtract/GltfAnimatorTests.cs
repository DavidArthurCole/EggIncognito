using System.IO.Compression;
using EggIncognito.Services.Assets;
using EggIncognito.Services.ProtoExtract;
using SharpGLTF.Schema2;

namespace EggIncognito.Tests.ProtoExtract;

// GltfAnimator bakes a rotation/hover animation into a decoded ship .glb, since the bundled ships are static
// meshes. Tests run against a synthetic .glb and, when present, a real device ship mesh.
public class GltfAnimatorTests
{
    private static byte[] SampleGlb()
    {
        var decode = RpoMeshDecoder.Decode(SampleRpo.Build(), "TestShip");
        Assert.True(decode.Ok, decode.Diagnostics);
        return decode.Glb!;
    }

    [Fact]
    public void Animate_SpinY_AddsRotationChannel()
    {
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin(6f));
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal("SpinY", r.AnimationName);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = Assert.Single(model.LogicalAnimations);
        Assert.Equal("SpinY", anim.Name);
        // The channel targets the mesh node's rotation, and the clip is ~6s.
        Assert.True(anim.Duration > 5.9f && anim.Duration < 6.1f, $"duration {anim.Duration}");
        Assert.Contains(anim.Channels, c => c.GetRotationSampler() is not null);
    }

    [Fact]
    public void Animate_HoverSpin_AddsRotationAndTranslation()
    {
        var r = GltfAnimator.Animate(SampleGlb(), new GltfAnimator.Options(GltfAnimator.AnimationKind.HoverSpin, 4f, 0.2f));
        Assert.True(r.Ok, r.Diagnostics);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = Assert.Single(model.LogicalAnimations);
        Assert.Contains(anim.Channels, c => c.GetRotationSampler() is not null);
        Assert.Contains(anim.Channels, c => c.GetTranslationSampler() is not null);
    }

    [Fact]
    public void Animate_RoundTripsGeometry()
    {
        // Animating must preserve the mesh: same primitive + a non-empty position accessor survive.
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);
        var model = ModelRoot.ParseGLB(r.Glb!);
        var mesh = Assert.Single(model.LogicalMeshes);
        var prim = Assert.Single(mesh.Primitives);
        Assert.NotNull(prim.GetVertexAccessor("POSITION"));
    }

    [Fact]
    public void Animate_OffCenterMesh_PivotsAtCentroid()
    {
        // SampleRpo verts span x[0..1], y[0..2] -> bbox center (0.5, 1, 0). After re-pivoting, the animated
        // node sits at the centroid and the mesh moved to an offset child, so the spin is about the center.
        var r = GltfAnimator.Animate(SampleGlb(), GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);

        var model = ModelRoot.ParseGLB(r.Glb!);
        var anim = model.LogicalAnimations[0];
        // the animated (rotation-channel) node carries the centroid translation.
        var rotNode = anim.Channels.First(c => c.GetRotationSampler() is not null).TargetNode;
        var t = rotNode.LocalTransform.Translation;
        Assert.True(System.MathF.Abs(t.X - 0.5f) < 1e-4f, $"pivot x {t.X}");
        Assert.True(System.MathF.Abs(t.Y - 1.0f) < 1e-4f, $"pivot y {t.Y}");
        // the mesh now lives on a child node offset by -center, so geometry world position is unchanged.
        Assert.Null(rotNode.Mesh);
        var child = rotNode.VisualChildren.Single();
        Assert.NotNull(child.Mesh);
        Assert.True(System.MathF.Abs(child.LocalTransform.Translation.X + 0.5f) < 1e-4f);
    }

    [Fact]
    public void Animate_BadInput_FailsCleanly()
    {
        var r = GltfAnimator.Animate([1, 2, 3], GltfAnimator.Options.Spin());
        Assert.False(r.Ok);
        Assert.Null(r.Glb);
    }

    [Fact]
    public void ParseKind_IsCaseInsensitive_DefaultsSpinY()
    {
        Assert.Equal(GltfAnimator.AnimationKind.HoverSpin, GltfAnimator.ParseKind("hoverspin"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinZ, GltfAnimator.ParseKind("SpinZ"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinY, GltfAnimator.ParseKind("nonsense"));
        Assert.Equal(GltfAnimator.AnimationKind.SpinY, GltfAnimator.ParseKind(null));
    }

    [Fact]
    public void Animate_RealDeviceShip_Spins()
    {
        var tgz = DeviceTarball();
        if (tgz is null) return; // fixture absent (CI): synthetic tests cover the logic

        var entries = ReadGzippedTar(tgz);
        var extract = RpoAssetExtractor.FromEntries(entries);
        var ship = extract.Assets.FirstOrDefault(a => a.Key == "ei_ship_bcr" && a.Decode.Ok);
        Assert.True(ship is not null, "ei_ship_bcr should decode from the device tarball");

        var r = GltfAnimator.Animate(ship!.Decode.Glb!, GltfAnimator.Options.Spin());
        Assert.True(r.Ok, r.Diagnostics);
        var model = ModelRoot.ParseGLB(r.Glb!);
        Assert.Single(model.LogicalAnimations);
        // geometry preserved: the real ship has thousands of verts.
        Assert.True(model.LogicalMeshes[0].Primitives[0].GetVertexAccessor("POSITION").Count > 100);
    }

    private static byte[]? DeviceTarball()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "egi-repos.tgz"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "egi-repos.tgz"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }

    private static IEnumerable<(string Name, byte[] Bytes)> ReadGzippedTar(byte[] tgz)
    {
        using var input = new MemoryStream(tgz);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gz.CopyTo(plain);
        return TarReader.Read(plain.ToArray()).Select(e => (e.Name, e.Bytes));
    }
}
