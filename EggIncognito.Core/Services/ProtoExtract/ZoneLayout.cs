namespace EggIncognito.Services.ProtoExtract;



//

public static class ZoneLayout
{
    public enum ZoneId { Silos, Habs, BackRow, MidRow, FrontRow }

   
    public sealed record Zone(ZoneId Id, float AnchorX, float AnchorZ, float Width, float Depth);

   
    public const float BackRowZ = -4f;
    public const float MidRowZ = 5f;
    public const float FrontRowZ = 10f;
    public const float RowDepthBand = 6f;
    public const float ZoneGapX = 2.5f;
    public const float CoreLeftX = 2f;
    public const float CoreWidth = 60f;

    public static readonly IReadOnlyDictionary<ZoneId, Zone> Zones = BuildZones();

    private static IReadOnlyDictionary<ZoneId, Zone> BuildZones()
    {
       
       
        var silos = new Zone(ZoneId.Silos, AnchorX: -35f, AnchorZ: -2f, Width: 30f, Depth: 9f);
       
        var habs = new Zone(ZoneId.Habs, AnchorX: -35f, AnchorZ: FarmLayout.HabRowZ - 2f, Width: 70f, Depth: 4f);

        var backRow = new Zone(ZoneId.BackRow, AnchorX: CoreLeftX, AnchorZ: BackRowZ, Width: CoreWidth, Depth: RowDepthBand);
        var midRow = new Zone(ZoneId.MidRow, AnchorX: CoreLeftX, AnchorZ: MidRowZ, Width: CoreWidth, Depth: RowDepthBand);
        var frontRow = new Zone(ZoneId.FrontRow, AnchorX: CoreLeftX, AnchorZ: FrontRowZ, Width: CoreWidth, Depth: RowDepthBand);

        return new Dictionary<ZoneId, Zone>
        {
            [ZoneId.Silos] = silos, [ZoneId.Habs] = habs,
            [ZoneId.BackRow] = backRow, [ZoneId.MidRow] = midRow, [ZoneId.FrontRow] = frontRow,
        };
    }

   
   
    public static IReadOnlyList<FarmLayout.Placed> Resolve(string lab, string hoa, string hatchery,
        string missionControl, string fuel, string depot)
    {
        FarmLayout.Placed At(ZoneId zone, string stem) => new(stem, [Zones[zone].AnchorX, 0f, Zones[zone].AnchorZ], 0, Recenter: true);

        return
        [
            At(ZoneId.BackRow, lab),
            At(ZoneId.BackRow, hoa),
            At(ZoneId.MidRow, hatchery),
            At(ZoneId.MidRow, missionControl),
            At(ZoneId.MidRow, fuel),
            At(ZoneId.FrontRow, depot),
        ];
    }

   
    public static bool IsInsideAnyZone(float x, float z)
    {
        foreach (var zone in Zones.Values)
        {
            if (x >= zone.AnchorX && x <= zone.AnchorX + zone.Width && z >= zone.AnchorZ && z <= zone.AnchorZ + zone.Depth)
                return true;
        }
        return false;
    }
}
