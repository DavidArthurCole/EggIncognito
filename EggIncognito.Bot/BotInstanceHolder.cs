namespace EggIncognito.Bot;

// Populated by EggIncognitoBotHostedService once SyncKitBot.StartAsync succeeds; null until then
// and forever if the bot never started (no token, or startup failed). Route handlers that need
// the live bot/client resolve it from here at request time, not at route-map time, since
// Program.cs maps routes before any IHostedService has run.
public sealed class BotInstanceHolder
{
    public SyncKit.Bot.SyncKitBot? Bot { get; set; }
}
