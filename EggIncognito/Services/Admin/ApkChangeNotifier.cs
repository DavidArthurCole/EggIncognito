using EggIncognito.Data.Services;

namespace EggIncognito.Services.Admin;

public sealed class ApkChangeNotifier(IServiceProvider services) : IApkStoreObserver {
    public Task OnChangedAsync(ApkStoreNotice notice, CancellationToken ct) {
        services.GetService<AdminNotifier>()?.Publish(AdminTopics.Apks);
        return Task.CompletedTask;
    }
}
