using System.Numerics;
using SharpGLTF.Schema2;

namespace EggIncognito.Services.Assets;

// Injects animation channels into a decoded ship .glb. The bundled ship meshes are static (RpoMeshDecoder
// emits geometry only); the game's "spinning ship" viewer animates them at runtime, and that animation does
// not exist as an asset. This generates it: a glTF rotation animation baked into the .glb so any glTF
// viewer (three.js, EggLedger) plays it without bespoke client code.
//
// Built on SharpGLTF (MIT). Toolkit, not a one-off: animations are a registry of named generators
// (AnimationKind), each producing keyframes for the target node. Add a kind by adding a generator; the
// endpoint + tests pick them up by name. Rotation keyframes are quaternions; a full turn is split into
// quarter steps so SLERP takes the intended direction (a single >180 deg step would reverse).
public static class GltfAnimator
{
    public enum AnimationKind
    {
        // Continuous spin about the vertical (Y) axis, looping. The conveyor/showcase rotation.
        SpinY,
        // Continuous spin about Z, for meshes authored with a different up axis.
        SpinZ,
        // Gentle bob (vertical translation) plus a slow Y spin, for an idle showcase.
        HoverSpin,
    }

    public sealed record Options(AnimationKind Kind = AnimationKind.SpinY, float DurationSeconds = 6f, float BobAmplitude = 0.15f)
    {
        public static Options Spin(float seconds = 6f) => new(AnimationKind.SpinY, seconds);
    }

    public sealed record Result(bool Ok, byte[]? Glb, string Diagnostics, string AnimationName, float DurationSeconds);

    // Reads a .glb, adds the requested animation to its first mesh node, writes a new .glb. Returns a failed
    // result (never throws) on malformed input or a model with no animatable node.
    public static Result Animate(byte[] glb, Options? options = null)
    {
        var opts = options ?? new Options();
        if (glb is null || glb.Length < 12) return Fail("input glb too short", opts);

        ModelRoot model;
        try { model = ModelRoot.ParseGLB(glb); }
        catch (Exception ex) { return Fail($"not a valid glb: {ex.Message}", opts); }

        var node = TargetNode(model);
        if (node is null) return Fail("glb has no node to animate", opts);

        var name = opts.Kind.ToString();
        try
        {
            ApplyAnimation(node, opts, name);
        }
        catch (Exception ex)
        {
            return Fail($"animation authoring failed: {ex.Message}", opts);
        }

        byte[] outGlb;
        try { outGlb = model.WriteGLB().ToArray(); }
        catch (Exception ex) { return Fail($"glb write failed: {ex.Message}", opts); }

        return new Result(true, outGlb, "ok", name, opts.DurationSeconds);
    }

    // The node carrying the mesh, preferring the first scene's first mesh node. Falls back to the first node
    // that has a mesh, then the first node. Animating the mesh node spins the geometry in place.
    private static Node? TargetNode(ModelRoot model)
    {
        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault();
        var fromScene = scene?.VisualChildren.FirstOrDefault(n => n.Mesh is not null)
                        ?? scene?.VisualChildren.FirstOrDefault();
        return fromScene
               ?? model.LogicalNodes.FirstOrDefault(n => n.Mesh is not null)
               ?? model.LogicalNodes.FirstOrDefault();
    }

    private static void ApplyAnimation(Node node, Options opts, string name)
    {
        var d = opts.DurationSeconds <= 0 ? 6f : opts.DurationSeconds;

        // Spin about the geometry's CENTER, not the node origin. EI ship meshes are authored offset from
        // their origin (placed on the farm plane), so rotating the node directly swings them around an
        // off-center pivot. Re-pivot: move the mesh into a child offset by -center and put the animated node
        // at +center, so the node's rotation axis passes through the centroid. A no-op when already centered.
        var pivot = RepivotToCenter(node);

        switch (opts.Kind)
        {
            case AnimationKind.SpinY:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
            case AnimationKind.SpinZ:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitZ, d));
                break;
            case AnimationKind.HoverSpin:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                pivot.WithTranslationAnimation(name, BobKeys(opts.BobAmplitude, d, pivot.LocalTransform.Translation));
                break;
            default:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
        }
    }

    // Restructures so the animated (returned) node's origin sits at the mesh centroid: the mesh moves to a
    // child translated by -center, the node is translated by its old translation + center. World placement is
    // unchanged; only the rotation pivot moves to the center. Returns the node to animate. If the mesh has no
    // position data or is already centered, returns the node unchanged.
    private static Node RepivotToCenter(Node node)
    {
        if (node.Mesh is null) return node;
        var center = MeshCenter(node.Mesh);
        if (center is null) return node;
        var c = center.Value;
        if (c.Length() < 1e-6f) return node; // already centered on origin

        var mesh = node.Mesh;
        var keepTranslation = node.LocalTransform.Translation;

        var child = node.CreateNode($"{node.Name ?? "mesh"}_geom");
        child.Mesh = mesh;
        child.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(null, null, null, -c);

        node.Mesh = null;
        node.LocalTransform = SharpGLTF.Transforms.AffineTransform.CreateFromAny(null, null, null, keepTranslation + c);
        return node;
    }

    // The bounding-box center of a mesh's POSITION data (min+max)/2, or null when it has no positions.
    private static Vector3? MeshCenter(Mesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var prim in mesh.Primitives)
        {
            var pos = prim.GetVertexAccessor("POSITION");
            if (pos is null) continue;
            foreach (var v in pos.AsVector3Array())
            {
                any = true;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }
        return any ? (min + max) * 0.5f : null;
    }

    // A full 360 deg turn as 5 quaternion keys (0, 90, 180, 270, 360). Each step is 90 deg so SLERP between
    // adjacent keys rotates the short way in the intended direction; the 0==360 endpoints make it loop seam-
    // lessly. The 270->360 closing key is required, else the last quarter would not be authored.
    private static (float, Quaternion)[] SpinKeys(Vector3 axis, float duration)
    {
        var a = Vector3.Normalize(axis);
        const int steps = 4;
        var keys = new (float, Quaternion)[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = duration * i / steps;
            var angle = MathF.PI * 2f * i / steps; // radians, 0..2pi
            keys[i] = (t, Quaternion.CreateFromAxisAngle(a, angle));
        }
        return keys;
    }

    // A vertical bob: up to +amplitude and back over the duration, sine-like via 3 keys (0, peak, 0). Offsets
    // are relative to baseTranslation (the node's resting position after re-pivoting), so the bob does not
    // snap the model back to the origin.
    private static (float, Vector3)[] BobKeys(float amplitude, float duration, Vector3 baseTranslation)
    {
        var half = duration / 2f;
        return
        [
            (0f, baseTranslation),
            (half, baseTranslation + new Vector3(0f, amplitude, 0f)),
            (duration, baseTranslation),
        ];
    }

    private static Result Fail(string why, Options opts) =>
        new(false, null, why, opts.Kind.ToString(), opts.DurationSeconds);

    // Parses an animation kind from a query/string, case-insensitive, defaulting to SpinY.
    public static AnimationKind ParseKind(string? s) =>
        Enum.TryParse<AnimationKind>(s, ignoreCase: true, out var k) ? k : AnimationKind.SpinY;
}
