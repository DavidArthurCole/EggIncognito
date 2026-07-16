namespace EggIncognito.Services.Backfill.Sources;
public sealed record ListedVersion(string AppVersion, DateTimeOffset? ReleaseDate, string? Changelog);
public interface IVersionListSource
{
    string Name { get; }
    string Platform { get; }
    Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct);
}
