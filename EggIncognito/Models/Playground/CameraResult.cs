namespace EggIncognito.Models.Playground;

public record CameraResult(
    bool Ok,
    string? Element,
    int Index,
    Vec3Dto? Focus,
    float Distance,
    float Height,
    string? Locator,
    string? Diagnostics);
