namespace EggIncognito.Capture;

public sealed record CaptureOverrideRequest(
    string Host,
    string Method,
    string Path,
    string? DataParam,
    byte[]? Body);

public sealed record CaptureOverrideResponse(
    byte[] Body,
    int StatusCode = 200,
    string ContentType = "application/x-www-form-urlencoded");

public interface ICaptureResponseSource {
    ValueTask<CaptureOverrideResponse?> TryAnswerAsync(CaptureOverrideRequest request, CancellationToken ct);
}
