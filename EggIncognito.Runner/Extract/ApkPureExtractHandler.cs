using System.Security.Cryptography;
using System.Text;
using EggIncognito.Core.Models;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

public sealed record ExtractResult(int Status, string? Build, string? ProtoSha, string? Error, string? Detail);

// On-demand extract from APKPure: download XAPK, pull arm split, extract+clean proto, emit event.
// Bearer + single-flight gate mirrors ResyncHandler.
public sealed class ApkPureExtractHandler(
    string secret, ApkPureDownloader downloader, PbtkProtoExtractor extractor,
    IClientVersionReader clientVersion, State.ClientVersionState cvState,
    Func<NewVersionEvent, Task> postEvent)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ExtractResult> HandleAsync(string? authHeader, string? appVersion)
    {
        if (!BearerMatches(authHeader))
            return new ExtractResult(401, null, null, "unauthorized", null);
        if (!_lock.Wait(0))
            return new ExtractResult(409, null, null, "an extract is already running", null);
        try
        {
            if (string.IsNullOrWhiteSpace(appVersion))
                return new ExtractResult(400, null, null, "appVersion required", null);

            var downloaded = await downloader.DownloadApkAsync(appVersion);
            if (downloaded is null)
                return new ExtractResult(502, null, null, "download failed", null);
            var armSplit = ApkPureDownloader.ExtractArmSplit(downloaded);
            if (armSplit is null)
                return new ExtractResult(502, null, null, "no arm split in download", null);

            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".apk");
            byte[] protoBytes;
            string? cv;
            try
            {
                await File.WriteAllBytesAsync(tmp, armSplit);
                protoBytes = extractor.Extract(tmp);
                cv = clientVersion.Read(tmp, cvState.Last());
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* best-effort temp cleanup */ }
            }
            if (cv is not null && int.TryParse(cv, out var cvNum)) cvState.Save(cvNum);

            var build = ApkVersionCode.Read(armSplit);
            var protoSha = Convert.ToHexString(SHA256.HashData(protoBytes)).ToLowerInvariant();
            await postEvent(new NewVersionEvent
            {
                Package = "com.auxbrain.egginc",
                Version = appVersion,
                AppVersion = appVersion,
                Build = build ?? "",
                ClientVersion = cv,
                ApkRef = $"apkpure:{appVersion}",
                ProtoSha = protoSha,
                Platform = "android",
                ProtoTextB64 = Convert.ToBase64String(protoBytes),
                DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            });
            return new ExtractResult(200, build, protoSha, null, "extracted and posted");
        }
        catch (Exception ex)
        {
            return new ExtractResult(500, null, null, ex.Message, null);
        }
        finally { _lock.Release(); }
    }

    private bool BearerMatches(string? header)
    {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
