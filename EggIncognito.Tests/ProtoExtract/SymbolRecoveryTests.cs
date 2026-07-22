using System.IO.Compression;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class SymbolRecoveryTests {

    private static uint Bl(long pc, long target) => 0x94000000u | (uint)(((target - pc) >> 2) & 0x03FFFFFF);
    private static uint Nop() => 0xD503201Fu;
    private static uint Ret() => 0xD65F03C0u;
    private static uint MovZ(int rd, uint imm16) => 0xD2800000u | ((imm16 & 0xFFFF) << 5) | (uint)(rd & 0x1F);

    private static byte[] Words(params uint[] ws) => [.. ws.SelectMany(BitConverter.GetBytes)];

    [Fact]
    public void Recover_Tier0_TransplantsAllSymbols_WhenTextIdentical() {
        var text = Words(MovZ(0, 1), MovZ(1, 2), Ret(), MovZ(2, 3), Ret());
        var vm = SyntheticMacho.TextVm;
        var syms = new[]
        {
            new SyntheticMacho.Sym("__ZN3Foo3barEv", vm),
            new SyntheticMacho.Sym("__ZN3Foo4quuxEv", vm + 12),
        };
        var refb = SyntheticMacho.Build(text, syms);
        var tgt = SyntheticMacho.Build(text, []);

        var r = SymbolRecovery.Recover(refb, tgt, ["Foo3bar", "Foo4quux"]);
        Assert.Equal("exact-transplant", r.Tier);
        Assert.Equal(2, r.Recovered);
        Assert.Equal(2, r.RequestedFound.Count);
        Assert.Empty(r.RequestedMissing);
        Assert.Contains(r.Symbols, s => s.Name == "__ZN3Foo3barEv" && s.Value == vm);
    }

    [Fact]
    public void Recover_Tier1_RecoversRelocatedUnchangedFunction_NotChangedOne() {
        var vm = SyntheticMacho.TextVm;


        var refFuncA = Words(Bl((long)vm, (long)vm + 0x500), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        var refFuncB = Words(MovZ(0, 7), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        var refText = refFuncA.Concat(refFuncB).ToArray();
        var refb = SyntheticMacho.Build(refText, new[]
        {
            new SyntheticMacho.Sym("__ZN6FuncA2goEv", vm),
            new SyntheticMacho.Sym("__ZN6FuncB2goEv", vm + (ulong)refFuncA.Length),
        });



        var pad = Words(Nop(), Nop());
        var tgtFuncA = Words(Bl((long)vm + 8, (long)vm + 0x900), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        var tgtFuncBChanged = Words(MovZ(0, 9), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        var tgtText = pad.Concat(tgtFuncA).Concat(tgtFuncBChanged).ToArray();
        var tgt = SyntheticMacho.Build(tgtText, []);

        var r = SymbolRecovery.Recover(refb, tgt, ["FuncA2go", "FuncB2go"]);
        Assert.Equal("content-hash", r.Tier);
        Assert.Contains(r.Symbols, s => s.Name == "__ZN6FuncA2goEv");
        Assert.DoesNotContain(r.Symbols, s => s.Name == "__ZN6FuncB2goEv");
        Assert.Contains("FuncA2go", r.RequestedFound);
        Assert.Contains("FuncB2go", r.RequestedMissing);

        var rec = r.Symbols.First(s => s.Name == "__ZN6FuncA2goEv");
        Assert.Equal(vm + 8, rec.Value);
    }

    [Fact]
    public void Recover_None_WhenTargetHasNoText() {
        var refb = SyntheticMacho.Build(Words(Ret()), new[] { new SyntheticMacho.Sym("__ZN1A1fEv", SyntheticMacho.TextVm) });
        var r = SymbolRecovery.Recover(refb, new byte[64], ["A1f"]);
        Assert.Equal("none", r.Tier);
        Assert.Contains("A1f", r.RequestedMissing);
    }

    [Fact]
    public void Recover_Real_SelfRecovery_IsExactTransplant() {
        var refb = RealSymbolized();
        if (refb is null) return;
        var r = SymbolRecovery.Recover(refb, refb, ["FarmScene10updateSilo", "GalaxyParticle7onBirth"]);
        Assert.Equal("exact-transplant", r.Tier);
        Assert.True(r.Recovered > 400_000, $"recovered={r.Recovered}");
        Assert.Equal(2, r.RequestedFound.Count);
        Assert.Empty(r.RequestedMissing);
    }



    [Fact]
    public void Recover_Real_AdjacentVersion_RecoversManyIncludingRealTargets() {
        var refb = RealSymbolized();
        var tgt = RealStrippedAdjacent();
        if (refb is null || tgt is null) return;

        var r = SymbolRecovery.Recover(refb, tgt, ["GalaxyParticle6update", "FarmScene10updateSilo"]);
        Assert.Equal("content-hash", r.Tier);
        Assert.True(r.Recovered > 10_000, $"recovered={r.Recovered}");

        Assert.Contains(r.Symbols, s => s.Name == "__ZN14GalaxyParticle6updateEP14ParticleSystemf");


        Assert.DoesNotContain(r.Symbols, s => s.Name == "__ZN9FarmScene10updateSiloEP14GameControlleri");
    }


    [Fact]
    public void ExtractWith_RecoveredSymbols_PullsConstantsFromStrippedBinary() {
        var refb = RealSymbolized();
        var tgt = RealStrippedAdjacent();
        if (refb is null || tgt is null) return;

        var r = SymbolRecovery.Recover(refb, tgt, ["GalaxyParticle6update"]);
        var rec = r.Symbols.FirstOrDefault(s => s.Name == "__ZN14GalaxyParticle6updateEP14ParticleSystemf");
        Assert.False(string.IsNullOrEmpty(rec.Name), "GalaxyParticle::update not recovered");

        var ex = FunctionConstantExtractor.ExtractWith(tgt, r.Symbols, ["GalaxyParticle6update"]);
        Assert.True(ex.Ok, ex.Diagnostics);
        Assert.Equal("__ZN14GalaxyParticle6updateEP14ParticleSystemf", ex.FunctionName);
        Assert.True(ex.Floats.Count + ex.Calls.Count > 0, "recovered function disassembled to nothing");
    }

    private static byte[]? RealSymbolized() => ExecFromIpa("com.auxbrain.egginc_1.35.6_und3fined.ipa");
    private static byte[]? RealStrippedAdjacent() => ExecFromIpa("Egg-Inc-IPAOMTK.COM_latest.ipa");

    private static byte[]? ExecFromIpa(string fileName) {
        string? dir = null;
        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (Directory.Exists(full)) { dir = full; break; }
        }
        if (dir is null) return null;
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return null;
        using var zip = ZipFile.OpenRead(path);
        var e = zip.Entries.FirstOrDefault(en => {
            var f = en.FullName;
            if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
            var i = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            var rest = f[(i + 5)..];
            return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
        });
        if (e is null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
