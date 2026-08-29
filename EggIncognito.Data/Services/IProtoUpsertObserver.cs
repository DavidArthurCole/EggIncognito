using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Data.Services;

public sealed record ProtoUpsertNotice(
    int ProtoVersionId,
    string Platform,
    string AppVersion,
    string Build,
    string? ClientVersion,
    string ProtoSha,
    bool HasProtoText,
    bool Created,
    bool ProtoChanged,
    VersionDelta Delta,
    string? PrevAppVersion,
    string? PrevBuild);

public interface IProtoUpsertObserver {
    Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct);
}
