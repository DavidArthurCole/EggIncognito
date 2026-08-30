namespace EggIncognito.Models.Devices;

public sealed record VirtualBridgeInstance(
    string InstanceId,
    string Kind,
    string Image,
    string State,
    string? AdbSerial,
    string? HostRef,
    DateTimeOffset CreatedAt,
    string? Note);
