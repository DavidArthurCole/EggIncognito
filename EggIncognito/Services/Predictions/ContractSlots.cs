using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions;

public static class ContractSlots {
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeOnly Noon = new(12, 0);
    // Stopgap: this weekday/kind grid is authored from observed release history, not extracted from game data.
    // Validated against stored contract_releases since 2024 (checked 2026-09-01): releases land Mon/Wed/Fri at 12:00 ET.
    // Replace by deriving the grid from contract_releases weekday/time clustering.
    private static readonly ContractSlotKind[] NoKinds = [];
    private static readonly ContractSlotKind[] MondayKinds = [ContractSlotKind.NewContract];
    private static readonly ContractSlotKind[] WednesdayKinds = [ContractSlotKind.Leggacy];

    private static readonly ContractSlotKind[] FridayKinds =
        [ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra];

    public static IReadOnlyList<(double Time, ContractSlotKind Kind)> Next(DateTimeOffset from, int horizonSlots) {
        var slots = new List<(double Time, ContractSlotKind Kind)>();
        if (horizonSlots <= 0) return slots;

        double after = UnixSeconds.FromTime(from);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, Zone).DateTime);
        int maxDays = 7 * Math.Clamp(horizonSlots, 1, 366) + 14;
        for (int i = 0; i < maxDays && slots.Count < horizonSlots; i++, day = day.AddDays(1)) {
            var kinds = KindsFor(day.DayOfWeek);
            if (kinds.Length == 0) continue;
            double time = SlotTime(day);
            if (time <= after) continue;
            foreach (var kind in kinds) slots.Add((time, kind));
        }
        return slots;
    }

    internal static bool IsGridSlot(double time, double toleranceSeconds) {
        if (!UnixSeconds.IsValid(time)) return false;
        var local = TimeZoneInfo.ConvertTime(UnixSeconds.ToTime(time), Zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        if (KindsFor(day.DayOfWeek).Length == 0) return false;
        return Math.Abs(time - SlotTime(day)) <= toleranceSeconds;
    }

    internal static ContractSlotKind[] KindsFor(DayOfWeek day) => day switch {
        DayOfWeek.Monday => MondayKinds,
        DayOfWeek.Wednesday => WednesdayKinds,
        DayOfWeek.Friday => FridayKinds,
        _ => NoKinds
    };

    private static double SlotTime(DateOnly day) =>
        UnixSeconds.FromTime(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(Noon), Zone)));
}
