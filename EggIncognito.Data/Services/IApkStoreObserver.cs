namespace EggIncognito.Data.Services;

public static class ApkChangeKinds {
    public const string Stored = "stored";
    public const string Deleted = "deleted";
}

public sealed record ApkStoreNotice(
    string Kind,
    string Platform,
    string Package,
    string AppVersion,
    string Build,
    int Splits);

public interface IApkStoreObserver {
    Task OnChangedAsync(ApkStoreNotice notice, CancellationToken ct);
}
