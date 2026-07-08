namespace EggIncognito.Services.Backfill.Sources;

// One version "seen in the wild": appVersion + optional release date + optional changelog.
public sealed record ListedVersion(string AppVersion, DateTimeOffset? ReleaseDate, string? Changelog);

// FetchAsync must be resilient: a fetch failure or layout change degrades to an empty list, never throws.
public interface IVersionListSource
{
    string Name { get; }
    string Platform { get; }
    Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct);
}
