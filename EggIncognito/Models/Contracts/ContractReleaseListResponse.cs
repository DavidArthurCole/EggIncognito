namespace EggIncognito.Models.Contracts;

public sealed record ContractReleaseListResponse(int Total, IReadOnlyList<ContractReleaseDto> Releases);
