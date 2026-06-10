namespace EggIncognito.Bot;

// ISyncNotifier lets the ingest service push a review alert through the bot without taking a
// discord.net dependency. Implemented in the web project, the same decoupling pattern as
// IStatusProvider.
public interface ISyncNotifier
{
    // NotifyAsync posts a short review alert. Outcome is a one-line human summary, e.g.
    // "regen staged for 1.34" or "proto changed for 1.35, refresh needed".
    Task NotifyAsync(string outcome, CancellationToken ct = default);
}
