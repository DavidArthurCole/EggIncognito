namespace EggIncognito.Services.Assets;

public static class ContractPalette {
    public const string NewContract = "#10b981";
    public const string Leggacy = "#f59e0b";
    public const string Prophecy = "#8b5cf6";

    public static string ColorFor(bool leggacy, int prophecyEggs) =>
        !leggacy ? NewContract : prophecyEggs > 0 ? Prophecy : Leggacy;

    public static string BarStyle(bool ultraOnly, bool leggacy, int prophecyEggs) {
        if (ultraOnly) return EventPalette.BarStyle(null, true);
        var color = ColorFor(leggacy, prophecyEggs);
        return $"--evt-color:{color};--evt-from:{color};--evt-to:{color};";
    }
}
