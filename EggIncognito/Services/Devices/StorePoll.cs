using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class StorePoll {
    public static async Task<StoreCheckResult> WaitForClimbAsync(
        string id, string label, string storeName, string before,
        Func<CancellationToken, Task<string?>> readInstalled,
        int pollSeconds, int pollAttempts, ILogger logger,
        Action<string>? progress, CancellationToken ct) {
        for (int attempt = 0; attempt < pollAttempts; attempt++) {
            if (ct.IsCancellationRequested) break;
            try {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
            } catch (OperationCanceledException) {
                break;
            }

            string? now = await readInstalled(ct);
            int n = attempt + 1;
            logger.LogInformation("device check-update: {Id} {Label} poll {N}/{Max} installed={Ver}",
                id, label, n, pollAttempts, now ?? "?");
            if (now is not null && DeviceParsing.CompareVersions(now, before) > 0) {
                progress?.Invoke($"{storeName} installed {now} (was {before})");
                logger.LogInformation("device check-update: {Id} {Label} climb {Before} -> {After}", id, label, before,
                    now);
                return new StoreCheckResult(true, before, now, true, true, "updated", $"updated {before} -> {now}");
            }

            progress?.Invoke($"waiting for {storeName} install… {n * pollSeconds}s elapsed (no change yet)");
        }

        string? last = await readInstalled(ct);
        logger.LogInformation("device check-update: {Id} {Label} up_to_date installed={Ver} (no climb in {Max}x{Sec}s)",
            id, label, last ?? "?", pollAttempts, pollSeconds);
        return new StoreCheckResult(true, before, last, false, false, "up_to_date",
            $"no update applied in {pollAttempts * pollSeconds}s (already current, or install still in flight)");
    }
}
