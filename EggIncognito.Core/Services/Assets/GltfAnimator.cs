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
        switch (opts.Kind)
        {
            case AnimationKind.SpinY:
                node.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
            case AnimationKind.SpinZ:
                node.WithRotationAnimation(name, SpinKeys(Vector3.UnitZ, d));
                break;
            case AnimationKind.HoverSpin:
                node.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                node.WithTranslationAnimation(name, BobKeys(opts.BobAmplitude, d));
                break;
            default:
                node.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
        }
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

    // A vertical bob: up to +amplitude and back over the duration, sine-like via 3 keys (0, peak, 0).
    private static (float, Vector3)[] BobKeys(float amplitude, float duration)
    {
        var half = duration / 2f;
        return
        [
            (0f, Vector3.Zero),
            (half, new Vector3(0f, amplitude, 0f)),
            (duration, Vector3.Zero),
        ];
    }

    private static Result Fail(string why, Options opts) =>
        new(false, null, why, opts.Kind.ToString(), opts.DurationSeconds);

    // Parses an animation kind from a query/string, case-insensitive, defaulting to SpinY.
    public static AnimationKind ParseKind(string? s) =>
        Enum.TryParse<AnimationKind>(s, ignoreCase: true, out var k) ? k : AnimationKind.SpinY;
}
