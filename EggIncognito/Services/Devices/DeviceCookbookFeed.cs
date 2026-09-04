using System.Text.Json;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceCookbookFeed(DeviceTimelineCache cache) {
    private const int Window = 50;

    public async Task<CookbookRunStatus?> RunAsync(string deviceId, long jobId, CancellationToken ct) {
        var history = await cache.HistoryAsync(deviceId, Window, DeviceJobKinds.Cookbook, ct);
        if (history.FirstOrDefault(j => j.Id == jobId) is not { } job) return null;

        var lines = await cache.LinesAsync(deviceId, job.Id, ct);
        return new CookbookRunStatus(
            job.Id, job.DeviceId, job.State, job.Outcome, job.Message, job.StartedAt, job.FinishedAt,
            job.State == DeviceJobStates.Running,
            [.. lines.Select(l => l.Text)],
            ParseSteps(job.Detail));
    }

    private static List<CookbookStepResult>? ParseSteps(string? detail) {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        try {
            using var doc = JsonDocument.Parse(detail);
            if (!doc.RootElement.TryGetProperty("steps", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<CookbookStepResult>();
            foreach (var el in arr.EnumerateArray()) {
                string stepId = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                string title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string? note = el.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : null;
                var status = el.TryGetProperty("status", out var s)
                             && Enum.TryParse<CookbookStepStatus>(s.GetString(), out var parsed)
                    ? parsed
                    : CookbookStepStatus.Ok;
                list.Add(new CookbookStepResult(stepId, title, status, note, []));
            }

            return list.Count > 0 ? list : null;
        } catch (JsonException) {
            return null;
        }
    }
}
