using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions;

public static class ContractSlots {
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeOnly Noon = new(12, 0);
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

    private static ContractSlotKind[] KindsFor(DayOfWeek day) => day switch {
        DayOfWeek.Monday => MondayKinds,
        DayOfWeek.Wednesday => WednesdayKinds,
        DayOfWeek.Friday => FridayKinds,
        _ => NoKinds
    };

    private static double SlotTime(DateOnly day) =>
        UnixSeconds.FromTime(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(Noon), Zone)));
}
