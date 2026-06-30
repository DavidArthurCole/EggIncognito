using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

// Resolves a function's VA on a STRIPPED binary by finding the function that materializes a known target address.
//
// The case: content-hash recovery (SymbolRecovery v2) recovers functions whose BODY is byte-stable across the
// adjacent versions. When addParticle's body changed but an associated closure/lambda inside it did NOT (the
// lambda recovered with a VA), we can still pin addParticle: it is the function that takes the lambda's address
// (adrp+add -> the lambda VA, to install it into a std::function). So scan every device function (boundaries from
// LC_FUNCTION_STARTS, which survives stripping), disassemble it, track adrp+add page math, and return the
// function-start whose body resolves the target VA. That start = the referencing function = addParticle.
//
// More generally this finds "who references address X", the inverse of a call-target lookup. Pure, over capstone,
// defensive (DllNotFound / malformed -> empty). Binary not executed.
public static class Arm64AddrRefResolver
{
    public readonly record struct Ref(ulong FunctionVa, ulong ReferencedVa, int HitCount);

    // Every function-start whose body materializes targetVa via adrp+add, nearest-enclosing-function attributed.
    // Ordered by hit count desc (the function that references it most is the strongest candidate).
    public static IReadOnlyList<Ref> FindReferrers(byte[] bin, ulong targetVa)
    {
        var outp = new List<Ref>();
        if (bin is null || bin.Length < 64) return outp;
        if (!MachoText.TryFindText(bin, out var textFileOff, out var textSize, out var textVmAddr)) return outp;

        var starts = MachoFunctionStarts.Read(bin); // file offsets, ascending
        if (starts.Count == 0) return outp;

        var slide = textVmAddr - (ulong)textFileOff; // fileOff + slide = VA
        var textEnd = textFileOff + textSize;

        try
        {
            using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
            cs.EnableInstructionDetails = true;

            for (int i = 0; i < starts.Count; i++)
            {
                int fnStart = starts[i];
                if (fnStart < textFileOff || fnStart >= textEnd) continue;
                int fnEnd = i + 1 < starts.Count ? Math.Min(starts[i + 1], textEnd) : textEnd;
                int len = fnEnd - fnStart;
                if (len < 8 || fnStart + len > bin.Length) continue;

                var hits = CountAddrRefs(cs, bin, fnStart, len, slide, targetVa);
                if (hits > 0)
                    outp.Add(new Ref((ulong)fnStart + slide, targetVa, hits));
            }
        }
        catch (DllNotFoundException) { return outp; }
        catch { return outp; }

        outp.Sort((a, b) => b.HitCount.CompareTo(a.HitCount));
        return outp;
    }

    // Disassemble one function range, track adrp (+ following add) page math, and count how many times the body
    // resolves an absolute address equal to targetVa (whether by adrp+add, or an adrp page that targetVa falls in
    // and a later add/ldr lands exactly on it).
    private static int CountAddrRefs(CapstoneArm64Disassembler cs, byte[] bin, int fileStart, int len, ulong slide, ulong targetVa)
    {
        var code = new byte[len];
        Array.Copy(bin, fileStart, code, 0, len);
        ulong startVa = (ulong)fileStart + slide;

        var page = new Dictionary<string, ulong>(); // reg -> resolved page/address
        int hits = 0;

        foreach (var insn in cs.Disassemble(code, (long)startVa))
        {
            var ops = insn.Details?.Operands;
            if (ops is null) continue;

            switch (insn.Id)
            {
                case Arm64InstructionId.ARM64_INS_ADRP:
                    if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                        && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } rd)
                        page[rd.Name] = (ulong)ops[1].Immediate;
                    break;

                case Arm64InstructionId.ARM64_INS_ADD:
                    if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                        && ops[2].Type == Arm64OperandType.Immediate && page.TryGetValue(addRn.Name, out var addBase))
                    {
                        var resolved = addBase + (ulong)ops[2].Immediate;
                        page[addRd.Name] = resolved;
                        if (resolved == targetVa) hits++;
                    }
                    else if (ops.Length >= 1 && ops[0].Register is { } addClobber)
                        page.Remove(addClobber.Name);
                    break;
            }
        }
        return hits;
    }
}
