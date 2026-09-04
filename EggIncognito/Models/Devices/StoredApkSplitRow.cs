namespace EggIncognito.Models.Devices;

public sealed record StoredApkSplitRow(
    string Split,
    string Sha256,
    long ByteSize,
    string? SourceDeviceId,
    DateTimeOffset CapturedAt);
