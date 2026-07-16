namespace EggIncognito.Services.Backfill.Sources;
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
