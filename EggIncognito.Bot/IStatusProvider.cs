namespace EggIncognito.Bot;

// Supplies a point-in-time StatusSnapshot to the bot's command handlers. Implemented in the web
// project (StatusSnapshotFactory), which can read IAppMode + the live capture session; the Bot
// library stays decoupled from the web host behind this seam.
public interface IStatusProvider
{
    StatusSnapshot Build();
}
