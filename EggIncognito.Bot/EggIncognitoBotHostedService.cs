using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncKit.Bot;

namespace EggIncognito.Bot;

// All gateway lifecycle, command registration, presence, and shared-role logic lives in SyncKit.Bot;
// this class only owns the instance's lifetime, deliberately bypassing SyncKitBotBuilder.RunAsync()
// (which owns its own WebApplication) since EggIncognito already hosts one and cannot run two.
public sealed class EggIncognitoBotHostedService(
    BotConfig cfg, ILogger<EggIncognitoBotHostedService> logger) : IHostedService
{
    private SyncKitBot? _bot;

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
