namespace EggIncognito.Models.Admin;

public sealed record DataStatusResponse(
    List<DataStatusGameDataRow> GameData,
    List<string> Missing,
    DataStatusConfig Config,
    List<DataStatusFixtureRow> Fixtures);
