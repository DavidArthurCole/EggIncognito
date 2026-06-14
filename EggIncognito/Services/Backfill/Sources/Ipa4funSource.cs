namespace EggIncognito.Services.Backfill.Sources;

// ipa4fun iOS history (https://www.ipa4fun.com/history/74815/) DEFERRED: naive fetches 403 (needs a
// real browser session, not just a UA). Stubbed behind IVersionListSource so it slots in later with no
// importer change. The real iOS history path is the jailbroken iPhone 8 farm. Returns empty + logs once.
public sealed class Ipa4funSource(ILogger<Ipa4funSource> logger) : IVersionListSource
{
    public string Name => "ipa4fun";
    public string Platform => "ios";

    public Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        logger.LogInformation("backfill: ipa4fun fetch not implemented (anti-scrape)");
        return Task.FromResult<IReadOnlyList<ListedVersion>>([]);
    }
}
