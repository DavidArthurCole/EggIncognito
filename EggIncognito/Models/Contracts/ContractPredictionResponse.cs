namespace EggIncognito.Models.Contracts;

public sealed record ContractPredictionResponse(double GeneratedAt, IReadOnlyList<ContractSlotPrediction> Slots);
