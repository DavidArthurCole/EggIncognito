namespace EggIncognito.Models.Data;

public sealed record DataSourceRow(
    string Id,
    string Group,
    string Url,
    string DisplayName,
    string? Description,
    string Provenance,
    string Access,
    string? Feed,
    bool AcceptsName,
    long? Bytes,
    DataSourceMeta? Meta,
    List<DataSourceRow>? Children);
