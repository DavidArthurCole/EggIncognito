namespace EggIncognito.Models.Devices;

public sealed record StoredApkVersionRow(
    string Platform,
    string Package,
    string AppVersion,
    string Build,
    long ByteSize,
    bool Installable,
    DateTimeOffset CapturedAt,
    List<StoredApkSplitRow> Splits);
