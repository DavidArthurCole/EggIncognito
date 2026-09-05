namespace EggIncognito.Core.Services.Devices;

public sealed record PifProfile(
    string Manufacturer,
    string Model,
    string Brand,
    string Product,
    string Device,
    string Release,
    string Id,
    string Incremental,
    string SecurityPatch,
    int DeviceInitialSdkInt,
    DateOnly? ReleasedOn,
    DateOnly? Expiry) {
    public const int LegacyInitialSdkInt = 32;

    public string Fingerprint => $"google/{Product}/{Device}:{Release}/{Id}/{Incremental}:user/release-keys";

    public bool Expired(DateOnly today) => Expiry is { } e && today > e;
}
