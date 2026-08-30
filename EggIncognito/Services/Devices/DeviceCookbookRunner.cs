using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceCookbookRunner(
    IDeviceCookbooks cookbooks,
    IDeviceFleet fleet,
    DeviceJobStore jobs,
    IServiceScopeFactory scopeFactory,
    ILogger<DeviceCookbookRunner> logger) {
    public async Task<DeviceTarget?> TargetAsync(string deviceId, CancellationToken ct) {
        var entry = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return entry is null ? null : new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);
    }

    public async Task<IReadOnlyList<DeviceCookbookInfo>> DescribeAsync(string deviceId, CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target) return [];
        return await cookbooks.DescribeAllAsync(target, ct);
    }

    public async Task<DeviceCookbookStart> StartAsync(string deviceId, DeviceCookbookRequest request, string trigger,
        CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target)
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.UnknownDevice, Error: "unknown device");

        if (cookbooks.Find(request.CookbookId) is not { } cookbook) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.UnknownCookbook,
                Error: $"unknown cookbook '{request.CookbookId}'");
        }

        var info = await cookbook.DescribeAsync(target, ct);
        if (!info.Available) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.Unavailable,
                Error: info.Unavailable ?? $"'{cookbook.Id}' is not available on this device");
        }

        var job = await jobs.TryStartAsync(deviceId, DeviceJobKinds.Cookbook, trigger,
            $"{cookbook.Id} starting...", ct);
        if (job is null) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.Busy,
                Error: "another job is already running on this device");
        }

        _ = Task.Run(() => RunDetachedAsync(job, target, request with { CookbookId = cookbook.Id }),
            CancellationToken.None);
        return new DeviceCookbookStart(DeviceCookbookStartOutcome.Started, job.Id);
    }

    private async Task RunDetachedAsync(JobRef job, DeviceTarget target, DeviceCookbookRequest request) {
        using var scope = scopeFactory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        try {
            var cookbook = cookbooks.Find(request.CookbookId)!;
            var context = new DeviceCookbookContext(target, request.Argument,
                line => scoped.ProgressAsync(job, line).GetAwaiter().GetResult());
            var run = await cookbook.RunAsync(context, CancellationToken.None);

            await scoped.FinishAsync(job, run.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error, Summarize(run),
                new DeviceJobFacts(Detail: new {
                    cookbook = run.CookbookId,
                    argument = request.Argument,
                    failedStep = run.FailedStep
                }), CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "cookbook: {Cookbook} on {Device} threw", request.CookbookId, job.DeviceId);
            await scoped.FailAsync(job, ex.Message, CancellationToken.None);
        }
    }

    private static string Summarize(DeviceCookbookRun run) {
        if (run.Ok) return run.Note is { Length: > 0 } ok ? $"{run.CookbookId}: {ok}" : $"{run.CookbookId} ok";
        string where = run.FailedStep is { Length: > 0 } step ? $" at {step}" : "";
        return $"{run.CookbookId} failed{where}: {run.Note ?? "no detail"}";
    }
}
