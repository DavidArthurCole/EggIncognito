namespace EggIncognito.Services;

public static class AutosaveRecency {
    public static bool IsFresh(long savedAtUnixMs, long nowUnixMs, int maxAgeMinutes = 30) {
        if (savedAtUnixMs <= 0) return false;
        long ageMs = nowUnixMs - savedAtUnixMs;
        return ageMs >= 0 && ageMs <= (long)maxAgeMinutes * 60_000;
    }
}
