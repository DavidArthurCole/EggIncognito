using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.ProtoExtract;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public sealed record HarvestOutcome(string Status, string Revision, string? Note, int Changed, int Skipped,
    int Failed);

public sealed class DeviceHarvester(
    IDevicePlatforms platforms,
    DeviceAssetStore assets,
    DeviceStateStore states,
    GameBinaryStore binaries,
    ILogger<DeviceHarvester> logger) {
    private const string FingerprintPrefix = DeviceAssetStore.FingerprintPrefix;

    public async Task<HarvestOutcome> RunAsync(DeviceTarget target, bool force, CancellationToken ct) {
        var platform = platforms.For(target.Platform);
        var probe = await platform.ProbeAsync(target, ct);
        var observed = new DeviceRevision(target.Platform, target.Package, probe.InstalledAppVersion,
            probe.InstalledBuild, null);
        var state = await states.ObserveAsync(target.Id, observed, ct);

        if (!probe.Reachable) {
            await states.FinishAsync(target.Id, HarvestStatus.Unreachable, probe.Note ?? "device unreachable",
                state.Revision, ct);
            return new HarvestOutcome(HarvestStatus.Unreachable, state.Revision, probe.Note, 0, 0, 0);
        }

        if (!force && string.Equals(state.HarvestedRevision, state.Revision, StringComparison.Ordinal)) {
            await states.FinishAsync(target.Id, HarvestStatus.Ok, "revision unchanged", state.Revision, ct);
            return new HarvestOutcome(HarvestStatus.Ok, state.Revision, "revision unchanged", 0, 0, 0);
        }

        var log = new List<DeviceHarvestLog>();
        int changed = 0, skipped = 0, failed = 0;

        foreach (var entry in platform.Manifest()) {
            if (!entry.Supported) {
                log.Add(Row(target.Id, state.Revision, entry, "unsupported", entry.UnsupportedNote, 0, null));
                skipped++;
                continue;
            }

            var fp = await platform.FingerprintAsync(target, entry, ct);
            string fpName = FingerprintPrefix + entry.Name;
            if (fp is { Ok: true, Value: { Length: > 0 } value } && !force) {
                var stored = await assets.GetAsync(DeviceAssetKinds.Manifest, fpName, target.Platform, ct);
                if (stored is not null && string.Equals(Text(stored.Bytes), value, StringComparison.Ordinal)) {
                    log.Add(Row(target.Id, state.Revision, entry, "unchanged", null, 0, value));
                    skipped++;
                    continue;
                }
            }

            var known = await assets.ShaManifestAsync(target.Platform, entry.Kind, ct);
            var batch = await platform.HarvestAsync(target, entry, known, ct);
            if (batch is not { Ok: true, Value: { } pulled }) {
                log.Add(Row(target.Id, state.Revision, entry, "failed", batch.Note, 0, null));
                failed++;
                continue;
            }

            int wrote = 0;
            long bytes = 0;
            foreach (var item in pulled.Items) {
                bool stored = entry.Kind == DeviceAssetKinds.Binary
                    ? await StoreBinaryAsync(target.Platform, state.AppVersion, item, ct)
                    : await assets.PutAsync(target.Platform, entry.Kind, item.Name, item.Bytes, item.ContentType,
                        state.AppVersion, ct);
                if (!stored) continue;
                wrote++;
                bytes += item.Bytes.LongLength;
            }

            if (pulled.FailedPulls > 0) {
                string note = $"{pulled.FailedPulls} of {pulled.Present.Count} pulls failed, {wrote} written";
                log.Add(Row(target.Id, state.Revision, entry, "failed", note, bytes, fp.Value));
                logger.LogWarning("harvest {Device} entry {Entry}: {Note}", target.Id, entry.Name, note);
                failed++;
                continue;
            }

            if (pulled.Authoritative && entry.Kind != DeviceAssetKinds.Binary)
                await assets.PruneAsync(target.Platform, entry.Kind, [.. pulled.Present], ct);

            if (fp is { Ok: true, Value: { Length: > 0 } fresh }) {
                await assets.PutAsync(target.Platform, DeviceAssetKinds.Manifest, fpName,
                    Encoding.UTF8.GetBytes(fresh), "text/plain", state.AppVersion, ct);
            }

            log.Add(Row(target.Id, state.Revision, entry, wrote > 0 ? "updated" : "unchanged", null, bytes,
                fp.Value));
            if (wrote > 0) changed++; else skipped++;
        }

        await states.LogAsync(log, ct);
        string status = failed == 0 ? HarvestStatus.Ok : changed > 0 ? HarvestStatus.Partial : HarvestStatus.Failed;
        string note = $"{changed} updated, {skipped} unchanged, {failed} failed";
        await states.FinishAsync(target.Id, status, note, state.Revision, ct);
        logger.LogInformation("harvest {Device} rev {Revision}: {Note}", target.Id, state.Revision[..12], note);
        return new HarvestOutcome(status, state.Revision, note, changed, skipped, failed);
    }

    private async Task<bool> StoreBinaryAsync(string platform, string? version, HarvestItem item,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(version)) return false;
        string sha = Hashes.Sha256Hex(item.Bytes);
        var img = BinaryImage.Load(item.Bytes);
        var syms = img?.Symbols ?? MachoSymbols.Read(item.Bytes);
        await binaries.PutAsync(platform, version, sha, item.Bytes, syms.Count, syms.Count, "harvest", ct);
        return true;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static DeviceHarvestLog Row(string deviceId, string revision, HarvestEntry entry, string outcome,
        string? note, long bytes, string? sha) => new() {
            DeviceId = deviceId,
            RanAt = DateTimeOffset.UtcNow,
            Revision = revision,
            Entry = entry.Name,
            Kind = entry.Kind,
            Outcome = outcome,
            Note = note,
            ByteSize = bytes,
            Sha256 = sha
        };
}
