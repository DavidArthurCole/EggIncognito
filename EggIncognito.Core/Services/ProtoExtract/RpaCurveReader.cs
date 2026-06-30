namespace EggIncognito.Services.ProtoExtract;

// Decodes the Egg Inc "RPA1" animation-curve format (the `.rpa` files under animations/, e.g. ui_float_up.rpa,
// ei_value_wiggle.rpa, afx_ship_takeoff.rpa). These are baked keyframe curves: the game samples them over time
// to drive value tweens + node motion. The floating hatchery sub-pieces (bolt/probe/rings) are animated by curves
// of this shape, so reading them lets the playground replay the real motion instead of a hand-authored spin.
//
// Format (little-endian), verified against ei_value_wiggle.rpa (61 keys) + ui_float_up.rpa:
//   0x00  char[4]  magic "RPA1"
//   0x04  int32    tracks (1 in the samples)
//   0x08  int32    reserved/flags
//   0x0C  int32    reserved/flags
//   0x10  int32    nKeys
//   0x14  int32    nComponents (3 in the samples; the value is a vec up to 3 wide)
//   0x18  key[nKeys], each = 4 floats = (time, c0, c1, c2). time is seconds, 0..1 at 1/60 steps in the samples.
//                   exactly (len - 0x18) / 16 == nKeys (no trailing bytes).
// Pure, defensive (bad magic/short -> Ok=false), binary not executed.
public static class RpaCurveReader
{
    public readonly record struct Key(float Time, float C0, float C1, float C2);

    public readonly record struct Curve(bool Ok, int Tracks, int Components, IReadOnlyList<Key> Keys, string Diagnostics)
    {
        public float Duration => Keys.Count == 0 ? 0 : Keys[^1].Time;

        // Sample component `comp` (0..2) at time t with linear interpolation between keys (the baked curve already
        // captures the easing in its dense keyframes, so linear-between-keys reproduces it). Clamps to the ends.
        public float Sample(float t, int comp = 0)
        {
            if (Keys.Count == 0) return 0;
            if (t <= Keys[0].Time) return Comp(Keys[0], comp);
            if (t >= Keys[^1].Time) return Comp(Keys[^1], comp);
            for (int i = 1; i < Keys.Count; i++)
            {
                if (t <= Keys[i].Time)
                {
                    var a = Keys[i - 1];
                    var b = Keys[i];
                    var span = b.Time - a.Time;
                    var u = span <= 0 ? 0 : (t - a.Time) / span;
                    return Comp(a, comp) + (Comp(b, comp) - Comp(a, comp)) * u;
                }
            }
            return Comp(Keys[^1], comp);
        }

        private static float Comp(Key k, int comp) => comp switch { 0 => k.C0, 1 => k.C1, 2 => k.C2, _ => 0 };
    }

    public static Curve Read(byte[] bin)
    {
        if (bin is null || bin.Length < 0x18) return new(false, 0, 0, [], "too short");
        if (!(bin[0] == 'R' && bin[1] == 'P' && bin[2] == 'A' && bin[3] == '1'))
            return new(false, 0, 0, [], "not RPA1");

        int tracks = I32(bin, 0x04);
        int nKeys = I32(bin, 0x10);
        int nComp = I32(bin, 0x14);
        if (nKeys < 0 || nKeys > 1_000_000) return new(false, tracks, nComp, [], $"implausible nKeys {nKeys}");

        long need = 0x18L + (long)nKeys * 16;
        if (need > bin.Length) return new(false, tracks, nComp, [], $"truncated: need {need}, have {bin.Length}");

        var keys = new Key[nKeys];
        int o = 0x18;
        for (int i = 0; i < nKeys; i++, o += 16)
            keys[i] = new Key(F32(bin, o), F32(bin, o + 4), F32(bin, o + 8), F32(bin, o + 12));

        return new(true, tracks, nComp, keys, "ok");
    }

    private static int I32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static float F32(byte[] b, int o) => BitConverter.ToSingle(b, o);
}
