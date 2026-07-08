namespace EggIncognito.Bot;

// Lets the ingest service push a review alert through the bot without a Discord.Net dependency.
public interface ISyncNotifier
{
    // Outcome is a one-line human summary, e.g. "regen staged for 1.34".
    Task NotifyAsync(string outcome, CancellationToken ct = default);
}
