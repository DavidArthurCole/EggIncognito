namespace EggIncognito.Models.Devices;

public sealed record StoredApkList(bool Ok, int Count, List<StoredApkVersionRow> Versions);
