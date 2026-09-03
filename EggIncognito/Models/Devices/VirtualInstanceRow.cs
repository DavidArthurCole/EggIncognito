namespace EggIncognito.Models.Devices;

public sealed record VirtualInstanceRow(
    string InstanceId,
    string Kind,
    string Image,
    string State,
    string? Target,
    string? DeviceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    string? Note,
    bool ContainerPresent,
    string? ContainerStatus,
    long Flows,
    string? LastFlowAt,
    string? Activity = null);
