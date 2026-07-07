using EggIncognito.Services.ProtoExtract;
using Xunit.Abstractions;

namespace EggIncognito.Tests;

// FINAL SPIKE OUTCOME (2026-06-16): the inline-disasm clientVersion heuristic does NOT work on the iOS
// Mach-O, PROVEN by cross-build discrimination, not just intuition.
//
// Oracle: elgranjero `ClientVersion: 72, AppVersion: 1.35.7` => the local 1.35.5..1.35.8 binaries are all
// clientVersion 72; the 1.28.7 / 1.29.3 binaries are an OLDER, LOWER clientVersion (~30s).
//
// Proof of no signal: MachoClientVersionReader's candidate set (small ints written by MOVZ;STR to a struct
// offset from >=3 sites) is BYTE-IDENTICAL between 1.28.7 and 1.29.3, and nearly identical up to 1.35.7.
// The set is just the common compiler constants (multiples of 4/8 + a few), present in EVERY build. With
// any prev the reader returns the nearest such constant >= prev. Tell it prev=71 and it answers 72 for
// 1.28.7 too (whose real clientVersion is ~30). It reflects prev back; it does not read the version.
//
// Other approaches also dead (see git/memory): LC_SYMTAB stripped (no builder-fn address); setter-call
// arg-register pattern drowned in size/length args; RTTI mangled-name string has zero ADRP+ADD xrefs;
// per-offset cross-build value tracking is noise churn. The real clientVersion is not present as a
// distinguishable inline constant. Only a dynamic hook (frida) could read it, and that crashes on spawn
// (anti-tamper) per the iOS pipeline notes.
//
// These tests ASSERT THE NEGATIVE so the dead end is regression-guarded. The Arm64Decoder / MachoText /
// MachoSymbols types are correct + unit-tested and kept for any future (likely dynamic) iOS approach.
// Skips when the machine-local binaries are absent.
public class MachoClientVersionSpikeTests(ITestOutputHelper output)
{
    private static readonly string Root =
        Environment.GetEnvironmentVariable("EGGINC_IOS_HISTORICAL_ROOT") ?? @"C:\Users\david\egginc-ios-extract\historical";
    private static readonly string V1287 = Path.Combine(Root, "Egg_INC_1.28.7", "Payload", "egginc.app", "egginc");
    private static readonly string V1293 = Path.Combine(Root, "EGG_INC_Hack_1.29.3", "Payload", "egginc.app", "egginc");

    [Fact]
    public void InlineHeuristic_ReflectsPrev_NotClientVersion()
    {
        if (!File.Exists(V1287)) { output.WriteLine($"SKIP: {V1287} absent"); return; }

        // 1.28.7 is an old, low clientVersion (~30s). Anchored with prev=71 (a 1.35.x prior), the reader
        // STILL answers 72 -- impossible if it read the real version. This is the core failure.
        var oldBuild = MachoClientVersionReader.Read(File.ReadAllBytes(V1287), previousClientVersion: 71);
        output.WriteLine($"1.28.7 read(prev=71) = {oldBuild.ClientVersion?.ToString() ?? "null"} (real ~30s)");
        Assert.Equal(72, oldBuild.ClientVersion); // the misfire: prev reflected back, not the true version
    }

    [Fact]
    public void CandidateSets_AreIdenticalAcrossDifferentVersions_NoSignal()
    {
        if (!File.Exists(V1287) || !File.Exists(V1293)) { output.WriteLine("SKIP: binaries absent"); return; }

        var a = MachoClientVersionReader.Read(File.ReadAllBytes(V1287), 71).Candidates;
        var b = MachoClientVersionReader.Read(File.ReadAllBytes(V1293), 71).Candidates;
        output.WriteLine($"1.28.7 candidates: [{string.Join(",", a)}]");
        output.WriteLine($"1.29.3 candidates: [{string.Join(",", b)}]");

        // Two genuinely different-clientVersion builds produce the SAME candidate set => the set carries no
        // version information; it is just common compiler constants.
        Assert.Equal(a, b);
    }
}
