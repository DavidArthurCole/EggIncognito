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
        byte[] text = Words(MovZ(0, 1), MovZ(1, 2), Ret(), MovZ(2, 3), Ret());
        const ulong vm = SyntheticMacho.TextVm;
        var syms = new[] {
            new SyntheticMacho.Sym("__ZN3Foo3barEv", vm),
            new SyntheticMacho.Sym("__ZN3Foo4quuxEv", vm + 12)
        };
        byte[] refb = SyntheticMacho.Build(text, syms);
        byte[] tgt = SyntheticMacho.Build(text, []);

        var r = SymbolRecovery.Recover(refb, tgt, ["Foo3bar", "Foo4quux"]);
        Assert.Equal("exact-transplant", r.Tier);
        Assert.Equal(2, r.Recovered);
        Assert.Equal(2, r.RequestedFound.Count);
        Assert.Empty(r.RequestedMissing);
        Assert.Contains(r.Symbols, s => s.Name == "__ZN3Foo3barEv" && s.Value == vm);
    }

    [Fact]
    public void Recover_Tier1_RecoversRelocatedUnchangedFunction_NotChangedOne() {
        const ulong vm = SyntheticMacho.TextVm;


        byte[] refFuncA = Words(Bl((long)vm, (long)vm + 0x500), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        byte[] refFuncB = Words(MovZ(0, 7), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        byte[] refText = [.. refFuncA, .. refFuncB];
        byte[] refb = SyntheticMacho.Build(refText, new[] {
            new SyntheticMacho.Sym("__ZN6FuncA2goEv", vm),
            new SyntheticMacho.Sym("__ZN6FuncB2goEv", vm + (ulong)refFuncA.Length)
        });


        byte[] pad = Words(Nop(), Nop());
        byte[] tgtFuncA = Words(Bl((long)vm + 8, (long)vm + 0x900), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        byte[] tgtFuncBChanged = Words(MovZ(0, 9), Nop(), Nop(), Nop(), Nop(), Nop(), Nop(), Ret());
        byte[] tgtText = [.. pad, .. tgtFuncA, .. tgtFuncBChanged];
        byte[] tgt = SyntheticMacho.Build(tgtText, []);

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
        byte[] refb = SyntheticMacho.Build(Words(Ret()),
            new[] { new SyntheticMacho.Sym("__ZN1A1fEv", SyntheticMacho.TextVm) });
        var r = SymbolRecovery.Recover(refb, new byte[64], ["A1f"]);
        Assert.Equal("none", r.Tier);
        Assert.Contains("A1f", r.RequestedMissing);
    }
}
