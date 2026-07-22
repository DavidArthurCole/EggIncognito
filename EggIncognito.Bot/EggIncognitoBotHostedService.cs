using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncKit.Bot;

namespace EggIncognito.Bot;


public sealed class EggIncognitoBotHostedService(
    BotConfig cfg, ILogger<EggIncognitoBotHostedService> logger) : IHostedService {
    public SyncKitBot? Bot { get; private set; }

    public async Task StartAsync(CancellationToken ct) {
        try { Bot = await SyncKitBot.StartAsync(cfg); } catch (Exception ex) { logger.LogError(ex, "bot: failed to start - continuing without the bot"); }
    }

    public async Task StopAsync(CancellationToken ct) {
        if (Bot is not null) await Bot.DisposeAsync();
    }
}
