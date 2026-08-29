namespace EggIncognito.Models.Contracts;

public sealed record ContractPredictionData(
    IReadOnlyDictionary<ContractSlotKind, IReadOnlyList<ContractCandidate>> Pools,
    IReadOnlyDictionary<ContractSlotKind, double> PoolGapSeconds);
