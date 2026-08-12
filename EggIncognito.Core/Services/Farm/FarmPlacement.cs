using Ei;

namespace EggIncognito.Core.Services.Farm;

public enum PlacementOrigin {
    Binary,
    Config,
    Fixture,
    Derived,
    Authored
}

public readonly record struct Vec3(float X, float Y, float Z) {
    public static readonly Vec3 Zero = new(0f, 0f, 0f);

    public Vec3 Plus(Vec3 o) => new(X + o.X, Y + o.Y, Z + o.Z);

    public float[] ToArray() => [X, Y, Z];
}

public sealed record PlacementProvenance(PlacementOrigin Origin, string? Locator = null, string? Method = null) {
    public static PlacementProvenance FromBinary(string locator, string method = "decoded") =>
        new(PlacementOrigin.Binary, locator, method);

    public static PlacementProvenance Derived(string method) => new(PlacementOrigin.Derived, null, method);

    public static PlacementProvenance Authored(string method) => new(PlacementOrigin.Authored, null, method);
}

public sealed record FarmPlacement(
    ShellDB.Types.FarmElement Element,
    ShellSpec.Types.AssetType? AssetType,
    int Index,
    Vec3 Pos,
    Vec3 RotDeg,
    float Scale,
    PlacementProvenance Provenance) {
    public string? Stem { get; init; }

    public static FarmPlacement At(ShellDB.Types.FarmElement element, ShellSpec.Types.AssetType type, int index,
        Vec3 pos, PlacementProvenance provenance) =>
        new(element, type, index, pos, Vec3.Zero, 1f, provenance);
}

public readonly record struct FarmExtents(float Lab, float Depot, float Hatchery, bool HatcheryResolved);

public sealed record FarmLayout(IReadOnlyList<FarmPlacement> Placements, FarmExtents Extents);
