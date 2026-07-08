using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

// Disassembles an arm64 function byte range and recovers the float/double constants it uses + the functions
// it calls. Two constant idioms: a memory-pool load (`adrp`+`add`+`ldr s0,[xN,#off]`, resolved via the __text
// slide) and an inline FP immediate (`fmov s0, #5.5`, baked into the instruction).
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
                    else if (ops.Length >= 1 && ops[0].Register is { } addClobber)
                        page.Remove(addClobber.Name);
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
                        // q = 128-bit vector load (Eigen Matrix/Vector const), 4 packed f32 lanes; d = f64; s = f32.
                        if (name.StartsWith('q') && fileOff >= 0 && fileOff + 16 <= bin.Length)
                            for (int lane = 0; lane < 4; lane++)
                                floats.Add(new FloatConst(va + (ulong)(lane * 4), BitConverter.ToSingle(bin, (int)fileOff + lane * 4), false));
                        else if (name.StartsWith('d') && fileOff >= 0 && fileOff + 8 <= bin.Length)
                            floats.Add(new FloatConst(va, BitConverter.ToDouble(bin, (int)fileOff), true));
                        else if (name.StartsWith('s') && fileOff >= 0 && fileOff + 4 <= bin.Length)
                            floats.Add(new FloatConst(va, BitConverter.ToSingle(bin, (int)fileOff), false));
                    }
                    else if (ops.Length >= 1 && ops[0].Register is { } ldDst && page.ContainsKey(ldDst.Name))
                        page.Remove(ldDst.Name);
                    break;

                case Arm64InstructionId.ARM64_INS_FMOV:
                    // Covers scalar (fmov s0/d0, #imm) and vector-broadcast (fmov v0.4s, #imm) forms. The dest
                    // register name gives the width: d = f64, s/v = f32.
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[1].Type == Arm64OperandType.FloatingPoint && ops[0].Register is { } fmovRd)
                    {
                        bool f64 = fmovRd.Name?.StartsWith('d') == true;
                        floats.Add(new FloatConst((ulong)insn.Address, ops[1].FloatingPoint, f64));
                    }
                    break;

                case Arm64InstructionId.ARM64_INS_MOVI:
                    // movi vN.T, #imm builds a vector immediate; only the unambiguous zero vector is recorded,
                    // non-zero bit-pattern immediates are not a clean float and are left out.
                    if (ops.Length == 2 && ops[1].Type == Arm64OperandType.Immediate && ops[1].Immediate == 0)
                        floats.Add(new FloatConst((ulong)insn.Address, 0.0, false));
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
