using EggIncognito.Core.Services.Farm;

namespace EggIncognito.Services.ProtoExtract;

public static class FarmPlacementExtractor {
    public const string HabLocator = "GameController::getHabPosition 0x10040593c";
    public const string SiloLocator = "FarmScene::updateSilo 0x10008e080";
    public const string TrophyLocator = "FarmScene::updateTrophyCase 0x10008bc0c";
    public const string LabLocator = "FarmScene::updateLab 0x10008efb0";
    public const string DepotLocator = "FarmScene::updateDepot 0x10008fb38";
    public const string HoaLocator = "FarmScene::hoaPos 0x1000a46e0";
    public const string MissionControlLocator = "FarmScene::missionControlPos 0x1000a4ba0";
    public const string FuelTankLocator = "FarmScene::fuelTankPos 0x1000a4ca0";
    public const string CameraFocusLocator = "FarmScene::getCameraFocus 0x1000a4790";
    public const string CameraInfoLocator = "FarmScene::getCameraInfo 0x1000a4e04";
    public const string RoadLocator = "VehicleManager::update 0x1008d6d58";
    public const string VehicleSlotLocator = "GameController::attemptHireVehicle 0x100415b40";
    public const string HabTableLocator = "habdata table 0x103146e60";
    public const string EggTableLocator = "eggdata record +0xb8";
    public const string VehicleTableLocator = "vehicledata record +0xe0";

    private const ulong HabAnchorAdrp = 0x10040593c;
    private const ulong HabRowZAt = 0x100405948;
    private const ulong HabGapAt = 0x100405a38;
    private const ulong HabGapDoubledAt = 0x100405c10;

    private const ulong SiloZOddAt = 0x10008e6d8;
    private const ulong SiloZEvenAt = 0x10008e6dc;
    private const ulong SiloStepAt = 0x10008e6f4;
    private const ulong SiloBaseAt = 0x10008e6f8;
    private const ulong SiloYAt = 0x10008e714;

    private const ulong TrophyCaseXyAdrp = 0x10008bf48;
    private const ulong TrophyCaseZAt = 0x10008bf54;
    private const ulong TrophyColumnStepAdrp = 0x10008c304;
    private const ulong TrophyOriginXAt = 0x10008c30c;
    private const ulong TrophyRowStepAdrp = 0x10008c314;
    private const ulong TrophyOriginYzAdrp = 0x10008c320;
    private const ulong TrophyColumnsAt = 0x10008c48c;
    private const ulong TrophyCountAt = 0x10008c344;
    private const ulong TrophyBonusScaleAt = 0x1000b1d44;
    private const ulong TrophyBonusXyAdrp = 0x1000b1d60;
    private const ulong TrophyBonusZAdrp = 0x1000b1d6c;

    private const ulong UpdateLabAt = 0x10008efb0;
    private const ulong UpdateDepotAt = 0x10008fb38;
    private const long LabExtentField = 0x3d0;
    private const long DepotExtentField = 0x3d4;
    private const int LabFirstAssetType = 110;
    private const int DepotFirstAssetType = 100;
    private const int LabTierCount = 6;
    private const int DepotTierCount = 7;

    private const ulong LabFocusXAt = 0x10008f014;
    private const ulong LabFocusYzAdrp = 0x10008f01c;
    private const ulong DepotFocusXAt = 0x10008fbb8;
    private const ulong DepotFocusYzAdrp = 0x10008fbc0;

    private const ulong HoaFloorAt = 0x1000a4738;
    private const ulong HoaHomeOffsetAt = 0x1000a4740;
    private const ulong HoaAltOffsetAt = 0x1000a475c;
    private const ulong HoaZAdrp = 0x1000a4774;

    private const ulong PoseLowAdrp = 0x1000a4be0;
    private const ulong PoseHighAdrp = 0x1000a4be8;
    private const ulong PoseTailAt = 0x1000a4bf4;
    private const ulong MissionControlBaseYzAdrp = 0x1000a4c38;
    private const ulong MissionControlOffsetAt = 0x1000a4c60;

    private const ulong FuelTankSpacingAdrp = 0x1000a4cf0;
    private const ulong FuelTankBaseOffsetAt = 0x1000a4d68;
    private const ulong FuelTankZeroAt = 0x1000a4d84;
    private const ulong FuelTankSelectAt = 0x1000a4d88;
    private const ulong FuelTankRowOffsetAt = 0x1000a4d98;
    private const ulong FuelTankZLockedAt = 0x1000a4dac;
    private const ulong FuelTankZUnlockedAt = 0x1000a4db8;
    private const int FuelTankSpacingCount = 6;

    private const ulong FocusTableAdrp = 0x1000a47d4;
    private const ulong FocusTargetAdrAt = 0x1000a47dc;
    private const ulong CameraDistanceAdrp = 0x1000a4e34;
    private const ulong CameraHeightAdrp = 0x1000a4e40;
    private const ulong CameraUiDivisorAt = 0x1000a4e68;
    private const ulong CameraUiHeightScaleAt = 0x1000a4e74;
    private const ulong CameraUiDistanceScaleAt = 0x1000a4e80;
    private const int CameraElements = 13;

    private const ulong RoadZeroVectorAt = 0x1008d71a4;
    private const ulong RoadZeroStoreAt = 0x1008d71a8;
    private const ulong RoadSpawnXAt = 0x1008d71d4;
    private const ulong RoadZAt = 0x1008d71dc;
    private const ulong RoadDespawnXAt = 0x1008d7478;
    private const ulong RoadMaxSpeedMultAt = 0x1008d74a0;
    private const ulong RoadDepotStopXAt = 0x1008d74a4;
    private const ulong RoadFollowGapAt = 0x1008d7620;
    private const ulong RoadRoundTripAt = 0x1008d7100;
    private const ulong RoadHyperloopIndexAt = 0x1008d7058;
    private const ulong VehicleEmptyIndexAt = 0x100415d28;

    private const int HenHouseElement = 1;
    private const int HatcheryElement = 10;
    private const int HoaElement = 11;
    private const int FuelTankElement = 13;
    private static readonly int[] StaticFocusElements = [3, 4, 5, 6, 7];

    public readonly record struct Result(
        bool Ok,
        FarmPlacementData Data,
        IReadOnlyList<string> Missing,
        string Diagnostics);

    public static Result Extract(byte[] bin,
        IReadOnlyList<HabCatalogExtractor.HabEntry> habs,
        IReadOnlyList<EggCatalogExtractor.EggEntry> eggs,
        IReadOnlyList<VehicleCatalogExtractor.VehicleEntry> vehicles,
        string binaryVersion) {
        if (BinaryImage.Load(bin) is not MachoImage macho)
            return new Result(false, new FarmPlacementData(), [], "farm placement locators are iOS Mach-O only");

        var im = new Arm64Image(bin, macho);
        var miss = new List<string>();

        var data = new FarmPlacementData {
            Habs = [
                .. habs.Select(h => new HabGeometry {
                    Index = h.Index,
                    Name = h.Name,
                    Width = h.Width,
                    Extent = h.Extent,
                    Depth = h.Depth
                })
            ],
            Eggs = [
                .. eggs.Select(e => new EggGeometry {
                    Index = e.Index,
                    Name = e.Name,
                    HatcheryExtent = e.HatcheryExtent
                })
            ],
            Vehicles = [
                .. vehicles.Select(v => new VehicleGeometry {
                    Index = v.Index,
                    Name = v.Name,
                    Length = v.Length
                })
            ],
            BinaryVersion = binaryVersion,
            Provenance = BuildProvenance()
        };

        data = ReadHabRow(im, miss, data);
        data = ReadSilos(im, miss, data);
        data = data with { Trophy = ReadTrophy(im, miss) };
        data = ReadExtentTables(im, miss, data);
        data = ReadSingletons(im, miss, data);
        data = ReadCamera(im, miss, data);
        data = data with { Road = ReadRoad(im, miss) };

        bool ok = miss.Count == 0 && data.IsComplete;
        string note = miss.Count == 0
            ? $"{data.Habs.Count} habs, {data.LabExtents.Count} lab tiers, {data.DepotExtents.Count} depot tiers, "
              + $"{data.Eggs.Count} eggs, {data.Vehicles.Count} vehicles"
            : $"{miss.Count} unreadable fields: {string.Join(", ", miss)}";
        return new Result(ok, data, miss, note);
    }

    private static Dictionary<string, PlacementProvenance> BuildProvenance() =>
        new(StringComparer.Ordinal) {
            ["habRow"] = PlacementProvenance.FromBinary(HabLocator),
            ["habs"] = PlacementProvenance.FromBinary(HabTableLocator),
            ["silos"] = PlacementProvenance.FromBinary(SiloLocator),
            ["trophy"] = PlacementProvenance.FromBinary(TrophyLocator),
            ["labExtents"] = PlacementProvenance.FromBinary(LabLocator),
            ["depotExtents"] = PlacementProvenance.FromBinary(DepotLocator),
            ["eggs"] = PlacementProvenance.FromBinary(EggTableLocator),
            ["missionControlPose"] = PlacementProvenance.FromBinary(MissionControlLocator),
            ["fuelTankSpacing"] = PlacementProvenance.FromBinary(FuelTankLocator),
            ["hoa"] = PlacementProvenance.FromBinary(HoaLocator),
            ["camera"] = PlacementProvenance.FromBinary(CameraFocusLocator),
            ["cameraUi"] = PlacementProvenance.FromBinary(CameraInfoLocator),
            ["vehicles"] = PlacementProvenance.FromBinary(VehicleTableLocator),
            ["road"] = PlacementProvenance.FromBinary(RoadLocator),
            ["emptyVehicleIndex"] = PlacementProvenance.FromBinary(VehicleSlotLocator)
        };

    private static FarmPlacementData ReadHabRow(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (im.TryPageRef(HabAnchorAdrp, out ulong anchorVa) && im.TryF32(anchorVa, out float anchorX)
            && im.TryF32(anchorVa + 4, out float anchorY)) {
            data = data with { HabAnchorX = anchorX, HabRowY = anchorY };
        } else {
            miss.Add("habAnchor");
        }

        if (Arm64Bits.TryConst(im, HabRowZAt, out ulong zBits, out _)) {
            data = data with { HabRowZ = Arm64Bits.F32(zBits) };
        } else {
            miss.Add("habRowZ");
        }

        if (Arm64Bits.TryFmovImm(im, HabGapAt, out double gap, out _)
            && Arm64Bits.TryFmovImm(im, HabGapDoubledAt, out double gapAgain, out _)
            && Math.Abs(gap - gapAgain) < 1e-9) {
            data = data with { HabGap = (float)gap };
        } else {
            miss.Add("habGap");
        }

        return data;
    }

    private static FarmPlacementData ReadSilos(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, SiloZOddAt, out double zOdd, out _)
            && Arm64Bits.TryFmovImm(im, SiloZEvenAt, out double zEven, out _)) {
            data = data with { SiloZOdd = (float)zOdd, SiloZEven = (float)zEven };
        } else {
            miss.Add("siloZ");
        }

        if (Arm64Bits.TryConst(im, SiloStepAt, out ulong stepBits, out bool stepIs64) && !stepIs64) {
            data = data with { SiloStepX = unchecked((int)(uint)stepBits) };
        } else {
            miss.Add("siloStepX");
        }

        if (Arm64Bits.TryConst(im, SiloBaseAt, out ulong baseBits, out bool baseIs64) && !baseIs64) {
            data = data with { SiloBaseX = unchecked((int)(uint)baseBits) };
        } else {
            miss.Add("siloBaseX");
        }

        if (im.TryWord(SiloYAt, out uint yWord) && Arm64Bits.TryStore(yWord, out var yStore)
            && yStore is { Rt: 31, Size: 2, Fp: false }) {
            data = data with { SiloY = 0f };
        } else {
            miss.Add("siloY");
        }

        return data;
    }

    private static TrophyGeometry ReadTrophy(Arm64Image im, List<string> miss) {
        var trophy = new TrophyGeometry();

        if (im.TryPageRef(TrophyCaseXyAdrp, out ulong caseVa) && im.TryF32(caseVa, out float caseX)
            && im.TryF32(caseVa + 4, out float caseY)
            && Arm64Bits.TryConst(im, TrophyCaseZAt, out ulong caseZBits, out _)) {
            trophy = trophy with { CasePos = new Vec3(caseX, caseY, Arm64Bits.F32(caseZBits)) };
        } else {
            miss.Add("trophyCasePos");
        }

        if (im.TryPageRef(TrophyColumnStepAdrp, out ulong stepVa) && im.TryF64(stepVa, out double columnStep)) {
            trophy = trophy with { ColumnStepX = (float)columnStep };
        } else {
            miss.Add("trophyColumnStepX");
        }

        if (Arm64Bits.TryConst(im, TrophyOriginXAt, out ulong originXBits, out _)) {
            trophy = trophy with { OriginX = Arm64Bits.F32(originXBits) };
        } else {
            miss.Add("trophyOriginX");
        }

        if (im.TryPageRef(TrophyRowStepAdrp, out ulong rowVa) && im.TryF64(rowVa, out double rowStepY)
            && im.TryF64(rowVa + 8, out double rowStepZ)) {
            trophy = trophy with { RowStepY = (float)rowStepY, RowStepZ = (float)rowStepZ };
        } else {
            miss.Add("trophyRowStep");
        }

        if (im.TryPageRef(TrophyOriginYzAdrp, out ulong originVa) && im.TryF32(originVa, out float originY)
            && im.TryF32(originVa + 4, out float originZ)) {
            trophy = trophy with { OriginY = originY, OriginZ = originZ };
        } else {
            miss.Add("trophyOriginYz");
        }

        if (im.TryWord(TrophyColumnsAt, out uint colWord) && Arm64Bits.TryAddShifted(colWord, out var add)
            && add.Rn == add.Rm && add.ShiftKind == 0) {
            trophy = trophy with { Columns = 1 + (1 << add.Amount) };
        } else {
            miss.Add("trophyColumns");
        }

        if (Arm64Bits.TryCmpImm(im, TrophyCountAt, out ulong count, out _, out _)) {
            trophy = trophy with { Count = (int)count };
        } else {
            miss.Add("trophyCount");
        }

        if (Arm64Bits.TryConst(im, TrophyBonusScaleAt, out ulong scaleBits, out _)) {
            trophy = trophy with { BonusScale = Arm64Bits.F32(scaleBits) };
        } else {
            miss.Add("trophyBonusScale");
        }

        if (im.TryPageRef(TrophyBonusXyAdrp, out ulong bonusXyVa) && im.TryF32(bonusXyVa + 8, out float bonusX)
            && im.TryF32(bonusXyVa + 12, out float bonusY) && im.TryPageRef(TrophyBonusZAdrp, out ulong bonusZVa)
            && im.TryF32(bonusZVa, out float bonusZ)) {
            trophy = trophy with { BonusPos = new Vec3(bonusX, bonusY, bonusZ) };
        } else {
            miss.Add("trophyBonusPos");
        }

        return trophy;
    }

    private static FarmPlacementData ReadExtentTables(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (Arm64Switch.TryExtents(im, UpdateLabAt, LabExtentField, LabFirstAssetType, LabTierCount,
                out float[] lab)) {
            data = data with { LabExtents = lab };
        } else {
            miss.Add("labExtents");
        }

        if (Arm64Switch.TryExtents(im, UpdateDepotAt, DepotExtentField, DepotFirstAssetType, DepotTierCount,
                out float[] depot)) {
            data = data with { DepotExtents = depot };
        } else {
            miss.Add("depotExtents");
        }

        if (TryFocusBase(im, LabFocusXAt, LabFocusYzAdrp, out var labFocus)) {
            data = data with { LabFocusBase = labFocus };
        } else {
            miss.Add("labFocusBase");
        }

        if (TryFocusBase(im, DepotFocusXAt, DepotFocusYzAdrp, out var depotFocus)) {
            data = data with { DepotFocusBase = depotFocus };
        } else {
            miss.Add("depotFocusBase");
        }

        return data;
    }

    private static bool TryFocusBase(Arm64Image im, ulong xAt, ulong yzAdrp, out Vec3 focus) {
        focus = Vec3.Zero;
        if (!Arm64Bits.TryConst(im, xAt, out ulong xBits, out _)) return false;
        if (!im.TryPageRef(yzAdrp, out ulong yzVa)) return false;
        if (!im.TryF32(yzVa, out float y) || !im.TryF32(yzVa + 4, out float z)) return false;
        focus = new Vec3(Arm64Bits.F32(xBits), y, z);
        return true;
    }

    private static FarmPlacementData ReadSingletons(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, HoaFloorAt, out double floor, out _)) {
            data = data with { SingletonFloor = (float)floor };
        } else {
            miss.Add("singletonFloor");
        }

        if (Arm64Bits.TryFmovImm(im, HoaHomeOffsetAt, out double homeOffset, out _)) {
            data = data with { HoaHomeOffset = (float)homeOffset };
        } else {
            miss.Add("hoaHomeOffset");
        }

        if (Arm64Bits.TryConst(im, HoaAltOffsetAt, out ulong altBits, out bool altIs64) && altIs64) {
            data = data with { HoaAltOffset = (float)Arm64Bits.F64(altBits) };
        } else {
            miss.Add("hoaAltOffset");
        }

        if (im.TryPageRef(HoaZAdrp, out ulong hoaZVa) && im.TryF32(hoaZVa + 4, out float hoaZ)) {
            data = data with { HoaZ = hoaZ };
        } else {
            miss.Add("hoaZ");
        }

        if (TryPoseTable(im, out var pose)) {
            data = data with { MissionControlPose = pose };
        } else {
            miss.Add("missionControlPose");
        }

        if (!TryVerifyMissionControlBase(im, data)) miss.Add("missionControlBaseYz");

        if (Arm64Bits.TryFmovImm(im, MissionControlOffsetAt, out double mcOffset, out _)
            && Arm64Bits.TryFmovImm(im, FuelTankRowOffsetAt, out double rowOffset, out _)
            && Math.Abs(mcOffset - rowOffset) < 1e-9) {
            data = data with { MissionControlOffset = (float)mcOffset };
        } else {
            miss.Add("missionControlOffset");
        }

        if (im.TryPageRef(FuelTankSpacingAdrp, out ulong spacingVa)
            && im.TryF32Table(spacingVa, FuelTankSpacingCount, out float[] spacing)) {
            data = data with { FuelTankSpacing = spacing };
        } else {
            miss.Add("fuelTankSpacing");
        }

        return ReadFuelTank(im, miss, data);
    }

    private static bool TryVerifyMissionControlBase(Arm64Image im, FarmPlacementData data) {
        if (!im.TryPageRef(MissionControlBaseYzAdrp, out ulong va) || !im.TryF32(va, out float y)
            || !im.TryF32(va + 4, out float z)) {
            return false;
        }

        return data.MissionControlPose.Count > 0
               && Math.Abs(data.MissionControlPose[0].Y - y) < 1e-6f
               && Math.Abs(data.MissionControlPose[0].Z - z) < 1e-6f;
    }

    private static FarmPlacementData ReadFuelTank(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, FuelTankBaseOffsetAt, out double baseOffset, out _)) {
            data = data with { FuelTankBaseOffset = (float)baseOffset };
            if (im.TryWord(FuelTankZeroAt, out uint zeroWord) && Arm64Bits.IsFmovZeroToFp(zeroWord)
                && im.TryWord(FuelTankSelectAt, out uint selectWord) && Arm64Bits.IsFcsel(selectWord)) {
                data = data with { FuelTankLockedExtra = (float)baseOffset };
            } else {
                miss.Add("fuelTankLockedExtra");
            }
        } else {
            miss.Add("fuelTankBaseOffset");
            miss.Add("fuelTankLockedExtra");
        }

        if (Arm64Bits.TryConst(im, FuelTankZUnlockedAt, out ulong unlockedBits, out _)
            && Arm64Bits.TryConst(im, FuelTankZLockedAt, out ulong lockedBits, out _)) {
            data = data with {
                FuelTankZUnlocked = Arm64Bits.F32(unlockedBits),
                FuelTankZLocked = Arm64Bits.F32(lockedBits)
            };
        } else {
            miss.Add("fuelTankZ");
        }

        return data;
    }

    private static bool TryPoseTable(Arm64Image im, out Vec3[] pose) {
        pose = [];
        if (!im.TryPageRef(PoseLowAdrp, out ulong lowVa) || !im.TryF32Table(lowVa, 4, out float[] low))
            return false;
        if (!im.TryPageRef(PoseHighAdrp, out ulong highVa) || !im.TryF32Table(highVa, 4, out float[] high))
            return false;
        if (!Arm64Bits.TryConst(im, PoseTailAt, out ulong tailBits, out _)) return false;
        pose = [
            new Vec3(low[0], low[1], low[2]),
            new Vec3(low[3], high[0], high[1]),
            new Vec3(high[2], high[3], Arm64Bits.F32(tailBits))
        ];
        return true;
    }

    private static FarmPlacementData ReadCamera(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (im.TryPageRef(CameraDistanceAdrp, out ulong distanceVa)
            && im.TryF32Table(distanceVa, CameraElements, out float[] distance)) {
            data = data with { CameraDistance = distance };
        } else {
            miss.Add("cameraDistance");
        }

        if (im.TryPageRef(CameraHeightAdrp, out ulong heightVa)
            && im.TryF32Table(heightVa, CameraElements, out float[] height)) {
            data = data with { CameraHeight = height };
        } else {
            miss.Add("cameraHeight");
        }

        if (Arm64Bits.TryConst(im, CameraUiDivisorAt, out ulong divisorBits, out _)) {
            data = data with { CameraUiDivisor = Arm64Bits.F32(divisorBits) };
        } else {
            miss.Add("cameraUiDivisor");
        }

        if (Arm64Bits.TryFmovImm(im, CameraUiHeightScaleAt, out double heightScale, out _)) {
            data = data with { CameraUiHeightScale = (float)heightScale };
        } else {
            miss.Add("cameraUiHeightScale");
        }

        if (Arm64Bits.TryConst(im, CameraUiDistanceScaleAt, out ulong distanceScaleBits, out _)) {
            data = data with { CameraUiDistanceScale = Arm64Bits.F32(distanceScaleBits) };
        } else {
            miss.Add("cameraUiDistanceScale");
        }

        return ReadFocusBlocks(im, miss, data);
    }

    private static FarmPlacementData ReadFocusBlocks(Arm64Image im, List<string> miss, FarmPlacementData data) {
        if (!im.TryPageRef(FocusTableAdrp, out ulong tableVa)
            || !Arm64Bits.TryAdr(im, FocusTargetAdrAt, out ulong targetBase, out _)) {
            miss.Add("cameraFocusBlocks");
            return data;
        }

        var focus = new Vec3[CameraElements];
        foreach (int element in StaticFocusElements) {
            if (!TryBlock(im, tableVa, targetBase, element, out ulong block)
                || !TryStaticFocus(im, block, out focus[element - 1])) {
                miss.Add($"cameraStaticFocus{element}");
            }
        }

        data = data with { CameraStaticFocus = focus };

        if (TryBlock(im, tableVa, targetBase, HenHouseElement, out ulong habBlock)
            && TryFirstFmov(im, habBlock, 16, out double habZ)) {
            data = data with { HabFocusOffset = new Vec3(0f, 0f, (float)habZ) };
        } else {
            miss.Add("habFocusOffset");
        }

        if (TryBlock(im, tableVa, targetBase, HatcheryElement, out ulong hatcheryBlock)
            && TryHatcheryFocus(im, hatcheryBlock, out var hatchery, out float pivot, out float scale)) {
            data = data with {
                HatcheryFocusBase = hatchery,
                FocusExtentPivot = pivot,
                FocusExtentScale = scale
            };
        } else {
            miss.Add("hatcheryFocusBase");
        }

        if (TryBlock(im, tableVa, targetBase, HoaElement, out ulong hoaBlock)
            && Arm64Bits.TryFirstBranch(im, hoaBlock, 48, out ulong merge)
            && Arm64Bits.TryFmovImm(im, merge, out double hoaExtra, out _)) {
            data = data with { HoaFocusExtra = (float)hoaExtra };
        } else {
            miss.Add("hoaFocusExtra");
        }

        if (TryBlock(im, tableVa, targetBase, FuelTankElement, out ulong fuelBlock)
            && im.TryPageRef(fuelBlock + 0xc, out ulong fuelVa) && im.TryF32(fuelVa, out float fuelX)
            && im.TryF32(fuelVa + 4, out float fuelY)
            && Arm64Bits.TryFmovImm(im, fuelBlock + 0x20, out double fuelZ, out _)) {
            data = data with { FuelTankFocusOffset = new Vec3(fuelX, fuelY, (float)fuelZ) };
        } else {
            miss.Add("fuelTankFocusOffset");
        }

        return data;
    }

    private static bool TryBlock(Arm64Image im, ulong tableVa, ulong targetBase, int element, out ulong block) {
        block = 0;
        if (!im.TryByte(tableVa + (ulong)(element - 1), out byte slot)) return false;
        block = targetBase + (ulong)(4 * slot);
        return true;
    }

    private static bool TryStaticFocus(Arm64Image im, ulong block, out Vec3 focus) {
        var regs = new ulong?[32];
        var parts = new float[3];
        bool wrote = false;
        for (int i = 0; i < 10; i++) {
            if (!im.TryWord(block + (ulong)(4 * i), out uint word)) break;
            if (Arm64Bits.TryMovWide(word, out int rd, out ulong value, out var kind, out _)) {
                regs[rd] = kind == Arm64Bits.MovKind.Movk ? Arm64Bits.Merge(regs[rd] ?? 0, word) : value;
                continue;
            }

            if (Arm64Bits.TryStore(word, out var store) && store is { Size: 2, Fp: false }
                && store.Rn != 31 && store.Offset is 0 or 4 or 8) {
                parts[store.Offset / 4] = store.Rt == 31 ? 0f : Arm64Bits.F32(regs[store.Rt] ?? 0);
                wrote = true;
                continue;
            }

            break;
        }

        focus = new Vec3(parts[0], parts[1], parts[2]);
        return wrote;
    }

    private static bool TryFirstFmov(Arm64Image im, ulong block, int limit, out double value) {
        for (int i = 0; i < limit; i++) {
            if (Arm64Bits.TryFmovImm(im, block + (ulong)(4 * i), out value, out _)) return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryHatcheryFocus(Arm64Image im, ulong block, out Vec3 focus, out float pivot,
        out float scale) {
        focus = Vec3.Zero;
        pivot = 0f;
        scale = 0f;
        if (!Arm64Bits.TryFmovImm(im, block + 0x08, out double negativePivot, out _)) return false;
        if (!Arm64Bits.TryFmovImm(im, block + 0x10, out double rawScale, out _)) return false;
        if (!Arm64Bits.TryFmovImm(im, block + 0x18, out double rawPivot, out _)) return false;
        if (Math.Abs(negativePivot + rawPivot) > 1e-9) return false;
        if (!Arm64Bits.TryConst(im, block + 0x28, out ulong zBits, out _)) return false;
        pivot = (float)rawPivot;
        scale = (float)rawScale;
        focus = new Vec3(pivot, 0f, Arm64Bits.F32(zBits));
        return true;
    }

    private static RoadGeometry ReadRoad(Arm64Image im, List<string> miss) {
        var road = new RoadGeometry();

        if (Arm64Bits.TryConst(im, RoadSpawnXAt, out ulong spawnBits, out _)) {
            road = road with { SpawnX = Arm64Bits.F32(spawnBits) };
        } else {
            miss.Add("roadSpawnX");
        }

        if (Arm64Bits.TryConst(im, RoadZAt, out ulong zBits, out _)) {
            road = road with { RoadZ = Arm64Bits.F32(zBits) };
        } else {
            miss.Add("roadZ");
        }

        if (im.TryWord(RoadZeroVectorAt, out uint zeroWord) && Arm64Bits.TryMoviZero(zeroWord, out int zeroRd)
            && im.TryWord(RoadZeroStoreAt, out uint zeroStore) && Arm64Bits.TryStur(zeroStore, out var stur)
            && stur.Rt == zeroRd && stur.Bytes == 16) {
            road = road with { RoadY = 0f };
        } else {
            miss.Add("roadY");
        }

        if (Arm64Bits.TryConst(im, RoadDepotStopXAt, out ulong stopBits, out _)) {
            road = road with { DepotStopX = Arm64Bits.F32(stopBits) };
        } else {
            miss.Add("roadDepotStopX");
        }

        if (Arm64Bits.TryConst(im, RoadDespawnXAt, out ulong despawnBits, out _)) {
            road = road with { DespawnX = Arm64Bits.F32(despawnBits) };
        } else {
            miss.Add("roadDespawnX");
        }

        if (Arm64Bits.TryFmovImm(im, RoadFollowGapAt, out double followGap, out _)) {
            road = road with { FollowGap = (float)followGap };
        } else {
            miss.Add("roadFollowGap");
        }

        if (Arm64Bits.TryFmovImm(im, RoadMaxSpeedMultAt, out double maxSpeed, out _)) {
            road = road with { MaxSpeedMult = (float)maxSpeed };
        } else {
            miss.Add("roadMaxSpeedMult");
        }

        if (Arm64Bits.TryConst(im, RoadRoundTripAt, out ulong roundBits, out bool roundIs64) && roundIs64) {
            road = road with { RoundTripSeconds = (float)Arm64Bits.F64(roundBits) };
        } else {
            miss.Add("roadRoundTripSeconds");
        }

        if (Arm64Bits.TryCmpImm(im, RoadHyperloopIndexAt, out ulong hyperloop, out _, out _)) {
            road = road with { HyperloopVehicleIndex = (int)hyperloop };
        } else {
            miss.Add("hyperloopVehicleIndex");
        }

        if (Arm64Bits.TryCmpImm(im, VehicleEmptyIndexAt, out ulong empty, out _, out _)) {
            road = road with { EmptyVehicleIndex = (int)empty };
        } else {
            miss.Add("emptyVehicleIndex");
        }

        return road;
    }
}
