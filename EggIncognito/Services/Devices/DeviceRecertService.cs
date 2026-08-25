using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceRecertService(
    IEnumerable<IDeviceUiDriver> uiDrivers,
    DeviceConfig deviceConfig,
    DeviceRecertConfig config,
    DeviceJobStore jobs,
    IDeviceConnectionFactory connections,
    ILogger<DeviceRecertService> logger) {
    private readonly IDeviceUiDriver? _ui =
        uiDrivers.FirstOrDefault(u => Platforms.Matches(u.Platform, Platforms.Android));

    public async Task<DeviceFlowResult> RecertAsync(string deviceId, string trigger, CancellationToken ct) {
        var device = deviceConfig.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return Refused("unknown device", "lookup");
        if (!Platforms.Matches(device.Platform, Platforms.Android)) return Refused("recert is android-only", "lookup");

        if (string.IsNullOrEmpty(config.KsuWebUiPackage)) {
            logger.LogWarning("recert: {Id} KsuWebUiPackage not configured", deviceId);
            return Refused("recert: KsuWebUiPackage not configured", "config");
        }

        if (_ui is null) return Refused("recert: no android ui driver registered", "config");

        var job = await jobs.TryStartAsync(deviceId, DeviceJobKinds.Recert, trigger, "recertifying...", ct);
        if (job is null) return Refused("another job is already running on this device", "busy");

        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);

        try {
            string? fileExpiry = config.ExpiryFilePath is { Length: > 0 } path
                ? await ReadExpiryFileAsync(target, path, ct)
                : null;

            var merged = await RunFlowAsync(target, ct);
            if (fileExpiry is not null) {
                var fields = new Dictionary<string, string>(merged.Fields) { [config.ExpiryFieldName] = fileExpiry };
                merged = merged with { Fields = fields };
            }

            string outcome = merged.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error;
            await jobs.FinishAsync(job, outcome, Summarize(merged), new DeviceJobFacts(Detail: merged.Fields), ct);
            return merged;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogError(ex, "recert: {Id} threw", deviceId);
            await jobs.FailAsync(job, ex.Message, ct);
            return Refused($"recert threw: {ex.Message}", "exception");
        }
    }

    public async Task<string?> ReadExpiryAsync(string deviceId, CancellationToken ct) {
        var device = deviceConfig.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null || !Platforms.Matches(device.Platform, Platforms.Android)) return null;
        if (string.IsNullOrEmpty(config.KsuWebUiPackage)) return null;

        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);

        if (config.ExpiryFilePath is { Length: > 0 } path) return await ReadExpiryFileAsync(target, path, ct);

        if (_ui is null) return null;
        UiSelector? selector = config.ExpiryFieldResourceId is { Length: > 0 } rid ? UiSelector.Id(rid)
            : config.ExpiryFieldText is { Length: > 0 } text ? UiSelector.Text(text)
            : null;
        if (selector is null) return null;

        var runner = new DeviceFlowRunner(_ui);
        DeviceFlowStep[] steps = [
            DeviceFlowSteps.LaunchApp(config.KsuWebUiPackage),
            DeviceFlowSteps.WaitForText(config.IntegrityHubLabel, timeoutSeconds: 30),
            DeviceFlowSteps.ReadField(config.ExpiryFieldName, selector)
        ];
        var result = await runner.RunAsync(target, steps, null, ct);
        return result.Fields.GetValueOrDefault(config.ExpiryFieldName);
    }

    public async Task<DeviceFlowResult> RunFlowAsync(DeviceTarget target, CancellationToken ct) {
        var runner = new DeviceFlowRunner(_ui!);
        var primary = await runner.RunAsync(target, AndroidRecertFlow.BuildPrimary(config), null, ct);

        DeviceFlowResult? fallback = null;
        if (!primary.Ok && !string.IsNullOrEmpty(config.MagiskPackage))
            fallback = await runner.RunAsync(target, AndroidRecertFlow.BuildFallback(config), null, ct);

        DeviceFlowResult? verify = null;
        var verifySteps = AndroidRecertFlow.BuildVerify(config);
        if (verifySteps.Count > 0) verify = await runner.RunAsync(target, verifySteps, null, ct);

        return Merge(primary, fallback, verify);
    }

    public static DeviceFlowResult Merge(DeviceFlowResult primary, DeviceFlowResult? fallback, DeviceFlowResult? verify) {
        bool ok = primary.Ok || (fallback?.Ok ?? false);

        var log = new List<string>(primary.Log);
        if (fallback is not null) log.AddRange(fallback.Log);
        if (verify is not null) log.AddRange(verify.Log);

        var fields = new Dictionary<string, string>(primary.Fields);
        if (fallback is not null) foreach (var kv in fallback.Fields) fields[kv.Key] = kv.Value;
        if (verify is not null) foreach (var kv in verify.Fields) fields[kv.Key] = kv.Value;

        var shots = new List<DeviceFlowShot>(primary.Shots);
        if (fallback is not null) shots.AddRange(fallback.Shots);
        if (verify is not null) shots.AddRange(verify.Shots);

        string? failedStep = ok ? null : fallback?.FailedStep ?? primary.FailedStep;

        return new DeviceFlowResult(ok, log, fields, shots, failedStep);
    }

    private async Task<string?> ReadExpiryFileAsync(DeviceTarget target, string path, CancellationToken ct) {
        try {
            var conn = connections.For(target)!;
            var r = await conn.ShellAsync($"cat {path}", ct);
            if (r.ExitCode != 0) return null;
            string trimmed = r.Stdout.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        } catch (Exception ex) {
            logger.LogDebug(ex, "recert: expiry file read failed");
            return null;
        }
    }

    private string Summarize(DeviceFlowResult r) {
        string baseMsg = r.Ok ? "recert ok" : $"recert failed at {r.FailedStep ?? "?"}";
        return r.Fields.TryGetValue(config.ExpiryFieldName, out var expiry) ? $"{baseMsg}; expiry {expiry}" : baseMsg;
    }

    private static DeviceFlowResult Refused(string note, string failedStep) =>
        new(false, [note], new Dictionary<string, string>(), [], failedStep);
}
