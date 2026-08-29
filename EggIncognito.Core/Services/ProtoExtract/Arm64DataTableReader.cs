using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64DataTableReader {
    public static ListResult List(byte[] bin, string[] nameNeedles, int maxInstructions = 512)
        => ListWith(bin, BinaryImage.Load(bin)?.Symbols ?? [], nameNeedles, maxInstructions);

    public static ListResult ListWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string[] nameNeedles,
        int maxInstructions = 512) {
        if (bin is null || bin.Length < 64) return new ListResult(false, "", 0, 0, [], "binary too short");
        var img = BinaryImage.Load(bin);
        if (img is null || !img.TryFindText(out int textFileOff, out _, out ulong textVmAddr))
            return new ListResult(false, "", 0, 0, [], "no __text section");
        if (!MachoSymbols.TryFindFunc(syms, nameNeedles, out var fn))
            return new ListResult(false, "", 0, 0, [], $"symbol not found: {string.Join("|", nameNeedles)}");

        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, textVmAddr, textFileOff, out byte[] code, out _))
            return new ListResult(false, fn.Name, fn.Start, fn.End, [], "function bounds out of range");

        using var cs = Arm64Decode.CreateDisassembler(details: false);
        var list = new List<Insn>();
        foreach (var insn in cs.Disassemble(code, (long)fn.Start)) {
            list.Add(new Insn((ulong)insn.Address, insn.Mnemonic ?? "", insn.Operand ?? ""));
            if (list.Count >= maxInstructions) break;
        }

        return new ListResult(true, fn.Name, fn.Start, fn.End, list, "ok");
    }

    public static ListResult ListRange(byte[] bin, ulong startVa, ulong endVa, int maxInstructions = 512) {
        if (bin is null || bin.Length < 64) return new ListResult(false, "", 0, 0, [], "binary too short");
        var img = BinaryImage.Load(bin);
        if (img is null || !img.TryFindText(out int textFileOff, out _, out ulong textVmAddr))
            return new ListResult(false, "", 0, 0, [], "no __text section");

        if (!Arm64Decode.SliceFunction(bin, startVa, endVa, textVmAddr, textFileOff, out byte[] code, out _))
            return new ListResult(false, "", startVa, endVa, [], "range out of bounds");

        using var cs = Arm64Decode.CreateDisassembler(details: false);
        var list = new List<Insn>();
        foreach (var insn in cs.Disassemble(code, (long)startVa)) {
            list.Add(new Insn((ulong)insn.Address, insn.Mnemonic ?? "", insn.Operand ?? ""));
            if (list.Count >= maxInstructions) break;
        }

        return new ListResult(true, "", startVa, endVa, list, "ok");
    }

    public static ScanResult Scan(byte[] bin, string[] nameNeedles)
        => ScanWith(bin, BinaryImage.Load(bin)?.Symbols ?? [], nameNeedles);

    public static ScanResult ScanWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string[] nameNeedles) {
        if (bin is null || bin.Length < 64) return new ScanResult(false, "", [], "binary too short");
        var img = BinaryImage.Load(bin);
        if (img is null || !img.TryFindText(out int textFileOff, out _, out ulong textVmAddr))
            return new ScanResult(false, "", [], "no __text section");
        if (!MachoSymbols.TryFindFunc(syms, nameNeedles, out var fn))
            return new ScanResult(false, "", [], $"symbol not found: {string.Join("|", nameNeedles)}");

        return ScanCode(bin, img, textFileOff, textVmAddr, fn.Name, fn.Start, fn.End);
    }

    public static ScanResult ScanRange(byte[] bin, ulong startVa, ulong endVa) {
        if (bin is null || bin.Length < 64) return new ScanResult(false, "", [], "binary too short");
        var img = BinaryImage.Load(bin);
        if (img is null || !img.TryFindText(out int textFileOff, out _, out ulong textVmAddr))
            return new ScanResult(false, "", [], "no __text section");
        if (endVa <= startVa) return new ScanResult(false, "", [], "empty range");
        return ScanCode(bin, img, textFileOff, textVmAddr, "range", startVa, endVa);
    }

    private static ScanResult ScanCode(byte[] bin, IBinaryImage img, int textFileOff, ulong textVmAddr,
        string label, ulong start, ulong end) {
        if (!Arm64Decode.SliceFunction(bin, start, end, textVmAddr, textFileOff, out byte[] code, out _))
            return new ScanResult(false, label, [], "function bounds out of range");

        using var cs = Arm64Decode.CreateDisassembler();

        var tracker = new Arm64PageTracker();
        var seen = new HashSet<ulong>();
        var addresses = new List<AddressRef>();

        void Record(ulong va, string via) {
            if (va == 0 || !seen.Add(va)) return;
            if (img is not null && img.TryVaToFileOffset(va, out _, out var owner))
                addresses.Add(new AddressRef(va, owner.Segment, owner.Name, via));
        }

        foreach (var insn in cs.Disassemble(code, (long)start)) {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            if (insn.Id is Arm64InstructionId.ARM64_INS_LDR or Arm64InstructionId.ARM64_INS_LDUR
                && ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                && ops[^1].Type == Arm64OperandType.Memory && ops[^1].Memory?.Base is { } memBase
                && tracker.TryGet(memBase.Name, out ulong pv)) {
                ulong va = pv + (ulong)ops[^1].Memory!.Displacement;
                Record(va, $"ldr [{memBase.Name}] @0x{insn.Address:x}");
            }

            if (tracker.Step(insn) is { } full) Record(full, $"adrp+add @0x{insn.Address:x}");
        }

        return new ScanResult(true, label, addresses, "ok");
    }

    public readonly record struct AddressRef(ulong Va, string Segment, string Section, string Via);

    public readonly record struct ScanResult(
        bool Ok,
        string FunctionName,
        IReadOnlyList<AddressRef> Addresses,
        string Diagnostics);

    public readonly record struct Insn(ulong Va, string Mnemonic, string Operands);

    public readonly record struct ListResult(
        bool Ok,
        string FunctionName,
        ulong Start,
        ulong End,
        IReadOnlyList<Insn> Instructions,
        string Diagnostics);
}
