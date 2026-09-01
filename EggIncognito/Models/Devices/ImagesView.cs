namespace EggIncognito.Models.Devices;

public sealed record ImagesView(
    bool BuildEnabled,
    string? ActiveTag,
    string ConfigImage,
    bool DockerOk,
    string? Note,
    IReadOnlyList<ImageRow> Images);
