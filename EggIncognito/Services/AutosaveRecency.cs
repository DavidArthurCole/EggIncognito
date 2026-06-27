namespace EggIncognito.Services;

// Decides whether a playground autosave is recent enough to silently restore. Pure so it is unit-testable
// without a browser. A future timestamp (clock skew) or a zero/missing one is never fresh.
public static class AutosaveRecency
{
    public static bool IsFresh(long savedAtUnixMs, long nowUnixMs, int maxAgeMinutes = 30)
    {
        if (savedAtUnixMs <= 0) return false;
        var ageMs = nowUnixMs - savedAtUnixMs;
        if (ageMs < 0) return false; // saved in the future: clock skew, treat as not fresh
        return ageMs <= (long)maxAgeMinutes * 60_000;
    }
}
