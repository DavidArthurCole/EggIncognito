using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

// Disassembles an arm64 function byte range and recovers the float/double constants it loads + the functions
// it calls. Tracks the adrp/add address-pair idiom so a `ldr s0, [xN, #off]` resolves to an absolute VA, then
// reads the f32/f64 from the binary via the __text slide. Reimplements in C# (over capstone) the logic the
// throwaway /tmp/disas.py harness used to recover the silo formula. No Python, no shell-out.
//
// capstone-net note: Arm64Operand is a tagged union; reading .Immediate/.Register/.Memory on the wrong .Type
// throws, so every access is guarded by op.Type. adrp's immediate is already the resolved absolute page.
public static class MachoArm64Disassembler
{
    public readonly record struct FloatConst(ulong Va, double Value, bool IsF64);
    public readonly record struct AnalysisResult(IReadOnlyList<FloatConst> Floats, IReadOnlyList<ulong> CallTargets);

    public static AnalysisResult Analyze(byte[] bin, ulong startVa, ulong endVa, ulong textVmAddr, int textFileOff)
    {
        var floats = new List<FloatConst>();
        var calls = new List<ulong>();
        var slide = textVmAddr - (ulong)textFileOff; // va - slide = file offset

        var startFile = (long)startVa - (long)slide;
        var len = (long)endVa - (long)startVa;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length) return new AnalysisResult(floats, calls);

        var code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);

        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        cs.EnableInstructionDetails = true;

        var page = new Dictionary<string, ulong>(); // register name -> resolved absolute address
        foreach (var insn in cs.Disassemble(code, (long)startVa))
        {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            switch (insn.Id)
            {
                case Arm64InstructionId.ARM64_INS_ADRP:
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } adrpRd)
                        page[adrpRd.Name] = (ulong)ops[1].Immediate;
                    break;

                case Arm64InstructionId.ARM64_INS_ADD:
                    if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                        && ops[2].Type == Arm64OperandType.Immediate && page.TryGetValue(addRn.Name, out var addBase))
                        page[addRd.Name] = addBase + (ulong)ops[2].Immediate;
                    break;

                case Arm64InstructionId.ARM64_INS_LDR:
                case Arm64InstructionId.ARM64_INS_LDUR:
                    if (ops.Length >= 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[^1].Type == Arm64OperandType.Memory && ops[0].Register is { } rt
                        && ops[^1].Memory?.Base is { } memBase && page.TryGetValue(memBase.Name, out var pv))
                    {
                        var va = pv + (ulong)ops[^1].Memory!.Displacement;
                        var fileOff = (long)va - (long)slide;
                        var name = rt.Name ?? "";
                        if (name.StartsWith('d') && fileOff >= 0 && fileOff + 8 <= bin.Length)
                            floats.Add(new FloatConst(va, BitConverter.ToDouble(bin, (int)fileOff), true));
                        else if (name.StartsWith('s') && fileOff >= 0 && fileOff + 4 <= bin.Length)
                            floats.Add(new FloatConst(va, BitConverter.ToSingle(bin, (int)fileOff), false));
                    }
                    break;

                case Arm64InstructionId.ARM64_INS_BL:
                    if (ops.Length == 1 && ops[0].Type == Arm64OperandType.Immediate)
                        calls.Add((ulong)ops[0].Immediate);
                    break;
            }
        }
        return new AnalysisResult(floats, calls);
    }
}
