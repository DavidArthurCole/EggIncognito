using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncKit.Bot;

namespace EggIncognito.Bot;


public sealed class EggIncognitoBotHostedService(
    BotConfig cfg, ILogger<EggIncognitoBotHostedService> logger) : IHostedService
{
    private SyncKitBot? _bot;

   
   
    public SyncKitBot? Bot => _bot;

    public async Task StartAsync(CancellationToken ct)
    {
        try { _bot = await SyncKitBot.StartAsync(cfg); }
        catch (Exception ex) { logger.LogError(ex, "bot: failed to start - continuing without the bot"); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_bot is not null) await _bot.DisposeAsync();
    }
}
