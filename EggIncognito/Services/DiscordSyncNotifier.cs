using EggIncognito.Bot;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Services;

// DiscordSyncNotifier sends sync review alerts. The bot (DiscordBotHostedService) currently owns only
// a gateway presence + slash commands with no outbound channel-post seam, so this first cut logs the
// outcome. Wire an actual Discord channel post once a target channel is configured for the bot.
public sealed class DiscordSyncNotifier(ILogger<DiscordSyncNotifier> logger) : ISyncNotifier
{
    public Task NotifyAsync(string outcome, CancellationToken ct = default)
    {
        logger.LogInformation("sync: {Outcome}", outcome);
        return Task.CompletedTask;
    }
}
