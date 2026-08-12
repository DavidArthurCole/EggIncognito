namespace EggIncognito.Core.Services.Farm;

public sealed record HabGeometry {
    public int Index { get; init; }
    public string? Name { get; init; }
    public double Width { get; init; }
    public double Extent { get; init; }
    public double Depth { get; init; }
}

public sealed record TrophyGeometry {
    public Vec3 CasePos { get; init; }
    public float ColumnStepX { get; init; }
    public float OriginX { get; init; }
    public float RowStepY { get; init; }
    public float OriginY { get; init; }
    public float RowStepZ { get; init; }
    public float OriginZ { get; init; }
    public int Columns { get; init; }
    public int Count { get; init; }
    public float BonusScale { get; init; }
    public Vec3 BonusPos { get; init; }
}

public sealed record EggGeometry {
    public int Index { get; init; }
    public string? Name { get; init; }
    public double HatcheryExtent { get; init; }
}

public sealed record VehicleGeometry {
    public int Index { get; init; }
    public string? Name { get; init; }
    public double Length { get; init; }
}

public sealed record RoadGeometry {
    public float SpawnX { get; init; }
    public float RoadZ { get; init; }
    public float RoadY { get; init; }
    public float DepotStopX { get; init; }
    public float DespawnX { get; init; }
    public float FollowGap { get; init; }
    public float MaxSpeedMult { get; init; }
    public float RoundTripSeconds { get; init; }
    public int HyperloopVehicleIndex { get; init; }
    public int EmptyVehicleIndex { get; init; }
}

public sealed record FarmPlacementData {
    public IReadOnlyList<HabGeometry> Habs { get; init; } = [];
    public float HabAnchorX { get; init; }
    public float HabRowY { get; init; }
    public float HabRowZ { get; init; }
    public float HabGap { get; init; }

    public float SiloStepX { get; init; }
    public float SiloBaseX { get; init; }
    public float SiloY { get; init; }
    public float SiloZEven { get; init; }
    public float SiloZOdd { get; init; }

    public TrophyGeometry Trophy { get; init; } = new();

    public IReadOnlyList<float> LabExtents { get; init; } = [];
    public IReadOnlyList<float> DepotExtents { get; init; } = [];
    public IReadOnlyList<EggGeometry> Eggs { get; init; } = [];

    public IReadOnlyList<Vec3> MissionControlPose { get; init; } = [];
    public IReadOnlyList<float> FuelTankSpacing { get; init; } = [];

    public float SingletonFloor { get; init; }
    public float HoaHomeOffset { get; init; }
    public float HoaAltOffset { get; init; }
    public float HoaZ { get; init; }
    public float MissionControlOffset { get; init; }
    public float FuelTankBaseOffset { get; init; }
    public float FuelTankLockedExtra { get; init; }
    public float FuelTankZUnlocked { get; init; }
    public float FuelTankZLocked { get; init; }

    public IReadOnlyList<float> CameraDistance { get; init; } = [];
    public IReadOnlyList<float> CameraHeight { get; init; } = [];
    public IReadOnlyList<Vec3> CameraStaticFocus { get; init; } = [];
    public Vec3 HabFocusOffset { get; init; }
    public Vec3 LabFocusBase { get; init; }
    public Vec3 DepotFocusBase { get; init; }
    public Vec3 HatcheryFocusBase { get; init; }
    public Vec3 FuelTankFocusOffset { get; init; }
    public float FocusExtentPivot { get; init; }
    public float FocusExtentScale { get; init; }
    public float HoaFocusExtra { get; init; }
    public float CameraUiDivisor { get; init; }
    public float CameraUiHeightScale { get; init; }
    public float CameraUiDistanceScale { get; init; }

    public IReadOnlyList<VehicleGeometry> Vehicles { get; init; } = [];
    public RoadGeometry Road { get; init; } = new();

    public string? BinaryVersion { get; init; }
    public IReadOnlyDictionary<string, PlacementProvenance> Provenance { get; init; } =
        new Dictionary<string, PlacementProvenance>(StringComparer.Ordinal);

    public bool IsComplete =>
        Habs.Count >= 19 && LabExtents.Count >= 6 && DepotExtents.Count >= 7
        && MissionControlPose.Count >= 3 && FuelTankSpacing.Count >= 3
        && CameraDistance.Count >= 13 && CameraHeight.Count >= 13;

    public double HabWidth(int tier) {
        foreach (var h in Habs) {
            if (h.Index == tier) return h.Width;
        }

        return 0d;
    }

    public double HabDepth(int tier) {
        foreach (var h in Habs) {
            if (h.Index == tier) return h.Depth;
        }

        return 0d;
    }

    public bool TryHatcheryExtent(int tableIndex, string protoEggName, out float extent) {
        foreach (var e in Eggs) {
            if (e.Index == tableIndex) {
                extent = (float)e.HatcheryExtent;
                return true;
            }
        }

        foreach (var e in Eggs) {
            if (e.Name is not null && NameKey(e.Name) == NameKey(protoEggName)) {
                extent = (float)e.HatcheryExtent;
                return true;
            }
        }

        extent = 0f;
        return false;
    }

    private static string NameKey(string s) {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }
}
