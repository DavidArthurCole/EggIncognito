using EggIncognito.Core.Services.Farm;

namespace EggIncognito.GameData;

public sealed record FarmVecRow(float? X, float? Y, float? Z);

public sealed record FarmHabRow(int? Index, string? Name, double? Width, double? Extent, double? Depth);

public sealed record FarmEggRow(int? Index, string? Name, double? HatcheryExtent);

public sealed record FarmVehicleRow(int? Index, string? Name, double? Length);

public sealed record FarmHabRowBlock(float? AnchorX, float? Y, float? Z, float? Gap);

public sealed record FarmSiloBlock(float? StepX, float? BaseX, float? Y, float? ZEven, float? ZOdd);

public sealed record FarmTrophyBlock(
    FarmVecRow? CasePos,
    float? ColumnStepX,
    float? OriginX,
    float? RowStepY,
    float? OriginY,
    float? RowStepZ,
    float? OriginZ,
    int? Columns,
    int? Count,
    float? BonusScale,
    FarmVecRow? BonusPos);

public sealed record FarmSingletonBlock(
    float? Floor,
    float? HoaHomeOffset,
    float? HoaAltOffset,
    float? HoaZ,
    float? MissionControlOffset,
    float? FuelTankBaseOffset,
    float? FuelTankLockedExtra,
    float? FuelTankZUnlocked,
    float? FuelTankZLocked);

public sealed record FarmCameraBlock(
    IReadOnlyList<float>? Distance,
    IReadOnlyList<float>? Height,
    IReadOnlyList<FarmVecRow>? StaticFocus,
    FarmVecRow? HabFocusOffset,
    FarmVecRow? LabFocusBase,
    FarmVecRow? DepotFocusBase,
    FarmVecRow? HatcheryFocusBase,
    FarmVecRow? FuelTankFocusOffset,
    float? FocusExtentPivot,
    float? FocusExtentScale,
    float? HoaFocusExtra,
    float? UiDivisor,
    float? UiHeightScale,
    float? UiDistanceScale);

public sealed record FarmRoadBlock(
    float? SpawnX,
    float? RoadZ,
    float? RoadY,
    float? DepotStopX,
    float? DespawnX,
    float? FollowGap,
    float? MaxSpeedMult,
    float? RoundTripSeconds,
    int? HyperloopVehicleIndex,
    int? EmptyVehicleIndex);

public sealed record FarmPlacementDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<FarmHabRow>? Habs,
    FarmHabRowBlock? HabRow,
    FarmSiloBlock? Silos,
    FarmTrophyBlock? Trophy,
    IReadOnlyList<float>? LabExtents,
    IReadOnlyList<float>? DepotExtents,
    IReadOnlyList<FarmEggRow>? Eggs,
    IReadOnlyList<FarmVecRow>? MissionControlPose,
    IReadOnlyList<float>? FuelTankSpacing,
    FarmSingletonBlock? Singletons,
    FarmCameraBlock? Camera,
    IReadOnlyList<FarmVehicleRow>? Vehicles,
    FarmRoadBlock? Road);

public static class FarmPlacementCatalog {
    public const string DocumentId = "farm-placement";
    public const int HabRows = 19;
    public const int LabTiers = 6;
    public const int DepotTiers = 7;
    public const int MissionControlTiers = 3;
    public const int CameraElements = 13;

    public static FarmPlacementData Parse(string json) {
        var file = GameDataJson.Deserialize<FarmPlacementDataFile>(json, "Farm placement");
        if (string.IsNullOrEmpty(file.BinaryVersion))
            throw new GameDataSchemaException("Farm placement missing binaryVersion.");

        var habs = Require(file.Habs, "habs");
        if (habs.Count < HabRows)
            throw new GameDataSchemaException($"Farm placement habs has {habs.Count} rows, expected {HabRows}.");

        var lab = Require(file.LabExtents, "labExtents");
        if (lab.Count != LabTiers)
            throw new GameDataSchemaException($"Farm placement labExtents has {lab.Count} rows, expected {LabTiers}.");

        var depot = Require(file.DepotExtents, "depotExtents");
        if (depot.Count != DepotTiers) {
            throw new GameDataSchemaException(
                $"Farm placement depotExtents has {depot.Count} rows, expected {DepotTiers}.");
        }

        var pose = Require(file.MissionControlPose, "missionControlPose");
        if (pose.Count != MissionControlTiers) {
            throw new GameDataSchemaException(
                $"Farm placement missionControlPose has {pose.Count} rows, expected {MissionControlTiers}.");
        }

        var spacing = Require(file.FuelTankSpacing, "fuelTankSpacing");
        if (spacing.Count < MissionControlTiers) {
            throw new GameDataSchemaException(
                $"Farm placement fuelTankSpacing has {spacing.Count} rows, expected at least {MissionControlTiers}.");
        }

        var habRow = Require(file.HabRow, "habRow");
        var silos = Require(file.Silos, "silos");
        var trophy = Require(file.Trophy, "trophy");
        var singletons = Require(file.Singletons, "singletons");
        var camera = Require(file.Camera, "camera");
        var road = Require(file.Road, "road");

        var distance = Require(camera.Distance, "camera.distance");
        var height = Require(camera.Height, "camera.height");
        var staticFocus = Require(camera.StaticFocus, "camera.staticFocus");
        RequireLength(distance.Count, CameraElements, "camera.distance");
        RequireLength(height.Count, CameraElements, "camera.height");
        RequireLength(staticFocus.Count, CameraElements, "camera.staticFocus");

        return new FarmPlacementData {
            Habs = [
                .. habs.Select(h => new HabGeometry {
                    Index = Number(h.Index, "habs.index"),
                    Name = h.Name,
                    Width = Number(h.Width, "habs.width"),
                    Extent = Number(h.Extent, "habs.extent"),
                    Depth = Number(h.Depth, "habs.depth")
                })
            ],
            HabAnchorX = Number(habRow.AnchorX, "habRow.anchorX"),
            HabRowY = Number(habRow.Y, "habRow.y"),
            HabRowZ = Number(habRow.Z, "habRow.z"),
            HabGap = Number(habRow.Gap, "habRow.gap"),
            SiloStepX = Number(silos.StepX, "silos.stepX"),
            SiloBaseX = Number(silos.BaseX, "silos.baseX"),
            SiloY = Number(silos.Y, "silos.y"),
            SiloZEven = Number(silos.ZEven, "silos.zEven"),
            SiloZOdd = Number(silos.ZOdd, "silos.zOdd"),
            Trophy = new TrophyGeometry {
                CasePos = Vector(trophy.CasePos, "trophy.casePos"),
                ColumnStepX = Number(trophy.ColumnStepX, "trophy.columnStepX"),
                OriginX = Number(trophy.OriginX, "trophy.originX"),
                RowStepY = Number(trophy.RowStepY, "trophy.rowStepY"),
                OriginY = Number(trophy.OriginY, "trophy.originY"),
                RowStepZ = Number(trophy.RowStepZ, "trophy.rowStepZ"),
                OriginZ = Number(trophy.OriginZ, "trophy.originZ"),
                Columns = Number(trophy.Columns, "trophy.columns"),
                Count = Number(trophy.Count, "trophy.count"),
                BonusScale = Number(trophy.BonusScale, "trophy.bonusScale"),
                BonusPos = Vector(trophy.BonusPos, "trophy.bonusPos")
            },
            LabExtents = [.. lab],
            DepotExtents = [.. depot],
            Eggs = [
                .. (file.Eggs ?? []).Select(e => new EggGeometry {
                    Index = Number(e.Index, "eggs.index"),
                    Name = e.Name,
                    HatcheryExtent = Number(e.HatcheryExtent, "eggs.hatcheryExtent")
                })
            ],
            MissionControlPose = [.. pose.Select(p => Vector(p, "missionControlPose"))],
            FuelTankSpacing = [.. spacing],
            SingletonFloor = Number(singletons.Floor, "singletons.floor"),
            HoaHomeOffset = Number(singletons.HoaHomeOffset, "singletons.hoaHomeOffset"),
            HoaAltOffset = Number(singletons.HoaAltOffset, "singletons.hoaAltOffset"),
            HoaZ = Number(singletons.HoaZ, "singletons.hoaZ"),
            MissionControlOffset = Number(singletons.MissionControlOffset, "singletons.missionControlOffset"),
            FuelTankBaseOffset = Number(singletons.FuelTankBaseOffset, "singletons.fuelTankBaseOffset"),
            FuelTankLockedExtra = Number(singletons.FuelTankLockedExtra, "singletons.fuelTankLockedExtra"),
            FuelTankZUnlocked = Number(singletons.FuelTankZUnlocked, "singletons.fuelTankZUnlocked"),
            FuelTankZLocked = Number(singletons.FuelTankZLocked, "singletons.fuelTankZLocked"),
            CameraDistance = [.. distance],
            CameraHeight = [.. height],
            CameraStaticFocus = [.. staticFocus.Select(f => Vector(f, "camera.staticFocus"))],
            HabFocusOffset = Vector(camera.HabFocusOffset, "camera.habFocusOffset"),
            LabFocusBase = Vector(camera.LabFocusBase, "camera.labFocusBase"),
            DepotFocusBase = Vector(camera.DepotFocusBase, "camera.depotFocusBase"),
            HatcheryFocusBase = Vector(camera.HatcheryFocusBase, "camera.hatcheryFocusBase"),
            FuelTankFocusOffset = Vector(camera.FuelTankFocusOffset, "camera.fuelTankFocusOffset"),
            FocusExtentPivot = Number(camera.FocusExtentPivot, "camera.focusExtentPivot"),
            FocusExtentScale = Number(camera.FocusExtentScale, "camera.focusExtentScale"),
            HoaFocusExtra = Number(camera.HoaFocusExtra, "camera.hoaFocusExtra"),
            CameraUiDivisor = Number(camera.UiDivisor, "camera.uiDivisor"),
            CameraUiHeightScale = Number(camera.UiHeightScale, "camera.uiHeightScale"),
            CameraUiDistanceScale = Number(camera.UiDistanceScale, "camera.uiDistanceScale"),
            Vehicles = [
                .. (file.Vehicles ?? []).Select(v => new VehicleGeometry {
                    Index = Number(v.Index, "vehicles.index"),
                    Name = v.Name,
                    Length = Number(v.Length, "vehicles.length")
                })
            ],
            Road = new RoadGeometry {
                SpawnX = Number(road.SpawnX, "road.spawnX"),
                RoadZ = Number(road.RoadZ, "road.roadZ"),
                RoadY = Number(road.RoadY, "road.roadY"),
                DepotStopX = Number(road.DepotStopX, "road.depotStopX"),
                DespawnX = Number(road.DespawnX, "road.despawnX"),
                FollowGap = Number(road.FollowGap, "road.followGap"),
                MaxSpeedMult = Number(road.MaxSpeedMult, "road.maxSpeedMult"),
                RoundTripSeconds = Number(road.RoundTripSeconds, "road.roundTripSeconds"),
                HyperloopVehicleIndex = Number(road.HyperloopVehicleIndex, "road.hyperloopVehicleIndex"),
                EmptyVehicleIndex = Number(road.EmptyVehicleIndex, "road.emptyVehicleIndex")
            },
            BinaryVersion = file.BinaryVersion,
            Provenance = MapProvenance(file.Provenance)
        };
    }

    private static IReadOnlyDictionary<string, PlacementProvenance> MapProvenance(
        IReadOnlyDictionary<string, ProvenanceSource>? source) {
        var map = new Dictionary<string, PlacementProvenance>(StringComparer.Ordinal);
        if (source is null) return map;
        foreach ((string key, var value) in source)
            map[key] = new PlacementProvenance(OriginOf(value.Origin), value.Locator, value.Method);
        return map;
    }

    private static PlacementOrigin OriginOf(string? origin) => origin switch {
        "binary" => PlacementOrigin.Binary,
        "config" => PlacementOrigin.Config,
        "fixture" => PlacementOrigin.Fixture,
        "derived" => PlacementOrigin.Derived,
        _ => PlacementOrigin.Authored
    };

    private static T Require<T>(T? value, string what) where T : class =>
        value ?? throw new GameDataSchemaException($"Farm placement missing {what}.");

    private static void RequireLength(int actual, int expected, string what) {
        if (actual != expected)
            throw new GameDataSchemaException($"Farm placement {what} has {actual} rows, expected {expected}.");
    }

    private static float Number(float? value, string what) =>
        value ?? throw new GameDataSchemaException($"Farm placement missing {what}.");

    private static double Number(double? value, string what) =>
        value ?? throw new GameDataSchemaException($"Farm placement missing {what}.");

    private static int Number(int? value, string what) =>
        value ?? throw new GameDataSchemaException($"Farm placement missing {what}.");

    private static Vec3 Vector(FarmVecRow? row, string what) {
        var value = Require(row, what);
        return new Vec3(Number(value.X, what + ".x"), Number(value.Y, what + ".y"), Number(value.Z, what + ".z"));
    }
}
