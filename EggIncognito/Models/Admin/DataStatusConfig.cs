namespace EggIncognito.Models.Admin;

public sealed record DataStatusConfig(bool Enabled, List<DataStatusConfigPlatform> Platforms);
