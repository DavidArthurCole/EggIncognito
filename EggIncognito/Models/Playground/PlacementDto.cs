namespace EggIncognito.Models.Playground;

public record PlacementDto(
    string Key,
    string? Element,
    string? AssetType,
    int Index,
    Vec3Dto? Pos,
    Vec3Dto? RotDeg,
    float Scale,
    string? Stem,
    string? ShellIdentifier,
    string? MeshUrl,
    ProvenanceDto? Provenance);
