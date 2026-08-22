using System.Text.Json;

namespace EggIncognito.Models.Playground;

public record LayoutResult(
    bool Ok,
    string? Platform,
    string? BinaryVersion,
    ExtentsDto? Extents,
    LayoutStateDto? State,
    LightingDto? Lighting,
    PlacementDto[]? Placements,
    JsonElement? Motion,
    string[]? Unresolved,
    string? Diagnostics);
