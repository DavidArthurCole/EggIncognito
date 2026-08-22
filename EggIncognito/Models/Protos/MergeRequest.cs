namespace EggIncognito.Models.Protos;

public sealed record MergeRequest(RegistryVersionKey Canonical, IReadOnlyList<RegistryVersionKey> Aliases);
