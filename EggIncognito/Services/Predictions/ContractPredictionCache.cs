using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Predictions;

public sealed class ContractPredictionCache {
    public long Version { get; set; } = -1;
    public ContractPredictionData? Value { get; set; }
}
