using Gee.External.Capstone.Arm64;

namespace EggIncognito.Services.ProtoExtract;

public sealed class Arm64PageTracker {
#pragma warning disable IDE0028
    private readonly Dictionary<string, ulong> _page = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public bool TryGet(string reg, out ulong va) => _page.TryGetValue(reg, out va);

    public ulong? Step(Arm64Instruction insn) {
        var ops = insn.Details?.Operands;
        if (ops is null) return null;

        string? kept = null;
        ulong? resolved = null;
        switch (insn.Id) {
            case Arm64InstructionId.ARM64_INS_ADRP:
                if (ops.Length == 2 && ops[0].Type == Arm64OperandType.Register
                                    && ops[1].Type == Arm64OperandType.Immediate && ops[0].Register is { } adrpRd) {
                    _page[adrpRd.Name] = (ulong)ops[1].Immediate;
                    kept = adrpRd.Name;
                }

                break;

            case Arm64InstructionId.ARM64_INS_ADD:
                if (ops.Length == 3 && ops[0].Register is { } addRd && ops[1].Register is { } addRn
                    && ops[2].Type == Arm64OperandType.Immediate &&
                    _page.TryGetValue(addRn.Name, out ulong addBase)) {
                    ulong full = addBase + (ulong)ops[2].Immediate;
                    _page[addRd.Name] = full;
                    kept = addRd.Name;
                    resolved = full;
                }

                break;
        }

        var written = insn.Details?.AllWrittenRegisters;
        if (written is not null) {
            foreach (var w in written) {
                if (w.Name is { } wn && wn != kept) _page.Remove(wn);
            }
        }

        return resolved;
    }
}
