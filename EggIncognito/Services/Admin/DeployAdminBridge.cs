using EggIdentity.Contract;
using EggIdentity.Deploy;

namespace EggIncognito.Services.Admin;

public sealed class DeployAdminBridge(IDeployEvents events, AdminNotifier notifier) : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) {
        events.Received += OnEvent;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        events.Received -= OnEvent;
        return Task.CompletedTask;
    }

    private void OnEvent(DeployEvent _) => notifier.Publish(AdminTopics.Deploy);
}
