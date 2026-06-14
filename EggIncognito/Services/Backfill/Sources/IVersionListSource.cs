namespace EggIncognito.Services.Backfill.Sources;

// One version "seen in the wild": appVersion + optional release date + optional changelog. No build/
// versionCode (no list source exposes it). The metadata-equivalent shape all adapters yield.
public sealed record ListedVersion(string AppVersion, DateTimeOffset? ReleaseDate, string? Changelog);

// A pluggable version-list source. Adding a future source = one adapter, no importer change. Each
// adapter pairs a thin FetchAsync (network) with a pure static parse method (unit-tested over a fixture).
// FetchAsync is resilient: a fetch failure or layout change degrades to an empty list + a logged
// warning, never throws into the importer.
public interface IVersionListSource
{
    string Name { get; }      // fandom | uptodown | apkpure | itunes | ipa4fun
    string Platform { get; }  // android | ios
    Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct);
}
