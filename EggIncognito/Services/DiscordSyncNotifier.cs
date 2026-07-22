using EggIncognito.Bot;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;


public sealed class DiscordSyncNotifier(ILogger<DiscordSyncNotifier> logger) : ISyncNotifier {
    public Task NotifyAsync(string outcome, CancellationToken ct = default) {
        logger.LogInformation("sync: {Outcome}", outcome);
        return Task.CompletedTask;
    }
}
