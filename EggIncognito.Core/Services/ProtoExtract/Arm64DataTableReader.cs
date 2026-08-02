using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

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

        ulong slide = textVmAddr - (ulong)textFileOff;
        long startFile = (long)fn.Start - (long)slide;
        long len = (long)fn.End - (long)fn.Start;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new ListResult(false, fn.Name, fn.Start, fn.End, [], "function bounds out of range");

        byte[] code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
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

        ulong slide = textVmAddr - (ulong)textFileOff;
        long startFile = (long)startVa - (long)slide;
        long len = (long)endVa - (long)startVa;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new ListResult(false, "", startVa, endVa, [], "range out of bounds");

        byte[] code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
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
        ulong slide = textVmAddr - (ulong)textFileOff;
        long startFile = (long)start - (long)slide;
        long len = (long)end - (long)start;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new ScanResult(false, label, [], "function bounds out of range");

        byte[] code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        cs.EnableInstructionDetails = true;

        var page = new Dictionary<string, ulong>();
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

            string? kept = null;
            switch (insn.Id) {
                case Arm64InstructionId.ARM64_INS_ADRP:
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                                        && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } adrpRd) {
                        page[adrpRd.Name] = (ulong)ops[1].Immediate;
                        kept = adrpRd.Name;
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_ADD:
                    if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                        && ops[2].Type == Arm64OperandType.Immediate &&
                        page.TryGetValue(addRn.Name, out ulong addBase)) {
                        ulong full = addBase + (ulong)ops[2].Immediate;
                        page[addRd.Name] = full;
                        kept = addRd.Name;
                        Record(full, $"adrp+add @0x{insn.Address:x}");
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_LDR:
                case Arm64InstructionId.ARM64_INS_LDUR:
                    if (ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                                        && ops[^1].Type == Arm64OperandType.Memory
                                        && ops[^1].Memory?.Base is { } memBase &&
                                        page.TryGetValue(memBase.Name, out ulong pv)) {
                        ulong va = pv + (ulong)ops[^1].Memory!.Displacement;
                        Record(va, $"ldr [{memBase.Name}] @0x{insn.Address:x}");
                    }

                    break;
            }

            var written = insn.Details?.AllWrittenRegisters;
            if (written is not null) {
                foreach (var w in written) {
                    if (w.Name is { } wn && wn != kept) page.Remove(wn);
                }
            }
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
