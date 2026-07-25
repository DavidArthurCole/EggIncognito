using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EggIdentity.Bot;

namespace EggIncognito.Bot;

public sealed class EggIncognitoBotHostedService(
    BotConfig cfg,
    ILogger<EggIncognitoBotHostedService> logger) : IHostedService {
    public EggIdentityBot? Bot { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken) {
        try {
            Bot = await EggIdentityBot.StartAsync(cfg);
        } catch (Exception ex) {
            logger.LogError(ex, "bot: failed to start - continuing without the bot");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        if (Bot is not null) await Bot.DisposeAsync();
    }
}
