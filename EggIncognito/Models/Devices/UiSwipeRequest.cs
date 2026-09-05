namespace EggIncognito.Models.Devices;

public sealed record UiSwipeRequest(int X1, int Y1, int X2, int Y2, int DurationMs = 200);
