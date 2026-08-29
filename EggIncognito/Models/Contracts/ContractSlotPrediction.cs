namespace EggIncognito.Models.Contracts;

public sealed record ContractSlotPrediction(
    double SlotTime, ContractSlotKind Kind, IReadOnlyList<ContractCandidate> Candidates);
