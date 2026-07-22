using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

public static class Arm64DataTableReader {
    public readonly record struct AddressRef(ulong Va, string Segment, string Section, string Via);
    public readonly record struct ScanResult(bool Ok, string FunctionName, IReadOnlyList<AddressRef> Addresses, string Diagnostics);

    public readonly record struct Insn(ulong Va, string Mnemonic, string Operands);
    public readonly record struct ListResult(bool Ok, string FunctionName, ulong Start, ulong End, IReadOnlyList<Insn> Instructions, string Diagnostics);

    public static ListResult List(byte[] bin, string[] nameNeedles, int maxInstructions = 512)
        => ListWith(bin, MachoSymbols.Read(bin), nameNeedles, maxInstructions);

    public static ListResult ListWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string[] nameNeedles, int maxInstructions = 512) {
        if (bin is null || bin.Length < 64) return new(false, "", 0, 0, [], "binary too short");
        if (!MachoText.TryFindText(bin, out var textFileOff, out _, out var textVmAddr))
            return new(false, "", 0, 0, [], "no __text section");
        if (!MachoSymbols.TryFindFunc(syms, nameNeedles, out var fn))
            return new(false, "", 0, 0, [], $"symbol not found: {string.Join("|", nameNeedles)}");

        var slide = textVmAddr - (ulong)textFileOff;
        var startFile = (long)fn.Start - (long)slide;
        var len = (long)fn.End - (long)fn.Start;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new(false, fn.Name, fn.Start, fn.End, [], "function bounds out of range");

        var code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        var list = new List<Insn>();
        foreach (var insn in cs.Disassemble(code, (long)fn.Start)) {
            list.Add(new Insn((ulong)insn.Address, insn.Mnemonic ?? "", insn.Operand ?? ""));
            if (list.Count >= maxInstructions) break;
        }
        return new(true, fn.Name, fn.Start, fn.End, list, "ok");
    }

    public static ListResult ListRange(byte[] bin, ulong startVa, ulong endVa, int maxInstructions = 512) {
        if (bin is null || bin.Length < 64) return new(false, "", 0, 0, [], "binary too short");
        if (!MachoText.TryFindText(bin, out var textFileOff, out _, out var textVmAddr))
            return new(false, "", 0, 0, [], "no __text section");

        var slide = textVmAddr - (ulong)textFileOff;
        var startFile = (long)startVa - (long)slide;
        var len = (long)endVa - (long)startVa;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new(false, "", startVa, endVa, [], "range out of bounds");

        var code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        var list = new List<Insn>();
        foreach (var insn in cs.Disassemble(code, (long)startVa)) {
            list.Add(new Insn((ulong)insn.Address, insn.Mnemonic ?? "", insn.Operand ?? ""));
            if (list.Count >= maxInstructions) break;
        }
        return new(true, "", startVa, endVa, list, "ok");
    }

    public static ScanResult Scan(byte[] bin, string[] nameNeedles)
        => ScanWith(bin, MachoSymbols.Read(bin), nameNeedles);

    public static ScanResult ScanWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string[] nameNeedles) {
        if (bin is null || bin.Length < 64) return new(false, "", [], "binary too short");
        if (!MachoText.TryFindText(bin, out var textFileOff, out _, out var textVmAddr))
            return new(false, "", [], "no __text section");
        if (!MachoSymbols.TryFindFunc(syms, nameNeedles, out var fn))
            return new(false, "", [], $"symbol not found: {string.Join("|", nameNeedles)}");

        var sections = MachoSections.Read(bin);
        var slide = textVmAddr - (ulong)textFileOff;
        var startFile = (long)fn.Start - (long)slide;
        var len = (long)fn.End - (long)fn.Start;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new(false, fn.Name, [], "function bounds out of range");

        var code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        cs.EnableInstructionDetails = true;

        var page = new Dictionary<string, ulong>();
        var seen = new HashSet<ulong>();
        var addresses = new List<AddressRef>();

        void Record(ulong va, string via) {
            if (va == 0 || !seen.Add(va)) return;
            if (MachoSections.TryVaToFileOffset(sections, va, out _, out var owner))
                addresses.Add(new AddressRef(va, owner.Segment, owner.Name, via));
        }

        foreach (var insn in cs.Disassemble(code, (long)fn.Start)) {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            switch (insn.Id) {
                case Arm64InstructionId.ARM64_INS_ADRP:
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } adrpRd) {
                        page[adrpRd.Name] = (ulong)ops[1].Immediate;
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_ADD:
                    if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                        && ops[2].Type == Arm64OperandType.Immediate && page.TryGetValue(addRn.Name, out var addBase)) {
                        var full = addBase + (ulong)ops[2].Immediate;
                        page[addRd.Name] = full;
                        Record(full, $"adrp+add @0x{insn.Address:x}");
                    } else if (ops.Length >= 1 && ops[0].Register is { } addClobber) {
                        page.Remove(addClobber.Name);
                    }

                    break;

                case Arm64InstructionId.ARM64_INS_LDR:
                case Arm64InstructionId.ARM64_INS_LDUR:
                    if (ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[^1].Type == Arm64OperandType.Memory && ops[0].Register is { } rt
                        && ops[^1].Memory?.Base is { } memBase && page.TryGetValue(memBase.Name, out var pv)) {
                        var va = pv + (ulong)ops[^1].Memory!.Displacement;
                        Record(va, $"ldr [{memBase.Name}] @0x{insn.Address:x}");
                        page.Remove(rt.Name ?? "");
                    } else if (ops.Length >= 1 && ops[0].Register is { } ldDst && page.ContainsKey(ldDst.Name)) {
                        page.Remove(ldDst.Name);
                    }

                    break;
            }
        }
        return new(true, fn.Name, addresses, "ok");
    }
}
