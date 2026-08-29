namespace EggIncognito.Models.Contracts;

public sealed record ContractNextEstimate(
    string ContractId, string Name, double LastReleased, double EstimatedNext, ContractSlotKind Pool, int Samples);
