using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Devices.Cookbooks;

namespace EggIncognito.Services.Devices;

public sealed class DeviceCookbookRunner(
    IDeviceCookbooks cookbooks,
    IDeviceFleet fleet,
    IDeviceProvisioners provisioners,
    VirtualDeviceConfig virtualConfig,
    DeviceJobStore jobs,
    CookbookExecutor executor,
    IServiceScopeFactory scopeFactory,
    ILogger<DeviceCookbookRunner> logger) {
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations =
        new(StringComparer.Ordinal);

    public bool TryCancel(string deviceId) {
        if (!_cancellations.TryGetValue(deviceId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public async Task<DeviceTarget?> TargetAsync(string deviceId, CancellationToken ct) {
        var entry = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        if (entry is not null) return new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);

        return await VirtualDeviceMirror.ResolveTargetAsync(provisioners, virtualConfig, deviceId, ct);
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

        var cts = new CancellationTokenSource();
        _cancellations[deviceId] = cts;
        _ = Task.Run(() => RunDetachedAsync(job, target, request with { CookbookId = cookbook.Id }, cts),
            CancellationToken.None);
        return new DeviceCookbookStart(DeviceCookbookStartOutcome.Started, job.Id);
    }

    public async Task<DeviceCookbookRun> RunNowAsync(string deviceId, DeviceCookbookRequest request, string trigger,
        CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target)
            return new DeviceCookbookRun(false, request.CookbookId, ["unknown device"], "target", "unknown device");

        if (cookbooks.Find(request.CookbookId) is not { } cookbook) {
            return new DeviceCookbookRun(false, request.CookbookId, [$"unknown cookbook '{request.CookbookId}'"],
                "cookbook", $"unknown cookbook '{request.CookbookId}'");
        }

        var job = await jobs.TryStartAsync(deviceId, DeviceJobKinds.Cookbook, trigger, $"{cookbook.Id} starting...", ct);
        if (job is null) {
            return new DeviceCookbookRun(false, cookbook.Id, ["another job is already running on this device"],
                "busy", "another job is already running on this device");
        }

        try {
            var context = new DeviceCookbookContext(target, request.Argument,
                line => jobs.ProgressAsync(job, line, ct: ct).GetAwaiter().GetResult());
            var run = await executor.RunAsync(cookbook, context, ct);
            await jobs.FinishAsync(job, run.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error, Summarize(run),
                Facts(run, request), CancellationToken.None);
            return run;
        } catch (Exception ex) {
            logger.LogError(ex, "cookbook: {Cookbook} on {Device} threw", request.CookbookId, deviceId);
            await jobs.FailAsync(job, ex.Message, CancellationToken.None);
            return new DeviceCookbookRun(false, cookbook.Id, [ex.Message], "exception", ex.Message);
        }
    }

    private async Task RunDetachedAsync(JobRef job, DeviceTarget target, DeviceCookbookRequest request,
        CancellationTokenSource cts) {
        using var scope = scopeFactory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        try {
            var cookbook = cookbooks.Find(request.CookbookId)!;
            var context = new DeviceCookbookContext(target, request.Argument,
                line => scoped.ProgressAsync(job, line).GetAwaiter().GetResult());
            var run = await executor.RunAsync(cookbook, context, cts.Token);

            await scoped.FinishAsync(job, run.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error, Summarize(run),
                Facts(run, request), CancellationToken.None);
        } catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            logger.LogInformation("cookbook: {Cookbook} on {Device} was stopped by an admin",
                request.CookbookId, job.DeviceId);
            await scoped.CancelAsync(job, $"{request.CookbookId} stopped by an admin", CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "cookbook: {Cookbook} on {Device} threw", request.CookbookId, job.DeviceId);
            await scoped.ProgressAsync(job, ex.ToString(), DeviceJobLevels.Error, CancellationToken.None);
            await scoped.FailAsync(job, ex.Message, CancellationToken.None);
        } finally {
            _cancellations.TryRemove(new KeyValuePair<string, CancellationTokenSource>(job.DeviceId, cts));
            cts.Dispose();
        }
    }

    private static DeviceJobFacts Facts(DeviceCookbookRun run, DeviceCookbookRequest request) =>
        new(Detail: new {
            cookbook = run.CookbookId,
            argument = request.Argument,
            failedStep = run.FailedStep,
            steps = run.Steps.Select(s => new {
                id = s.StepId,
                title = s.Title,
                status = s.Status.ToString(),
                note = s.Note
            })
        });

    private static string Summarize(DeviceCookbookRun run) {
        if (run.Ok) return run.Note is { Length: > 0 } ok ? ok : "ok";
        string where = run.FailedStep is { Length: > 0 } step ? $" at {step}" : "";
        return run.Note is { Length: > 0 } detail ? $"{detail}{where}" : $"failed{where}";
    }
}
