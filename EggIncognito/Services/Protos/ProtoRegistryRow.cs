using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Protos;

public sealed record ProtoRegistryRow(
    long Id,
    long? CanonicalId,
    string Platform,
    string? AppVersion,
    string Build,
    string? ClientVersion,
    string? Source,
    string? Package,
    string? ProtoSha,
    DateTime? DetectedAt,
    string? BuildFlag,
    int? SortOrder) {
    public VersionKey Key() {
        return new VersionKey(Platform, AppVersion, Build, ClientVersion, SortOrder, DetectedAt,
            CanonicalId ?? Id, ProtoSha);
    }
}
