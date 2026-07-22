namespace EggIncognito.Services;

public static class AutosaveRecency {
    public static bool IsFresh(long savedAtUnixMs, long nowUnixMs, int maxAgeMinutes = 30) {
        if (savedAtUnixMs <= 0) return false;
        var ageMs = nowUnixMs - savedAtUnixMs;
        return ageMs < 0 ? false : ageMs <= (long)maxAgeMinutes * 60_000;
    }
}
