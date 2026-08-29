namespace EggIncognito.Models.Devices;

public sealed record UiDumpResult(int Width, int Height, int Count, IReadOnlyList<UiNodeRow> Nodes);
