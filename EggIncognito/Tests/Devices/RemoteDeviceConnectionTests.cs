using System.Net;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class RemoteDeviceConnectionTests {
    private static DeviceTransportConfig Config() => new() {
        Mode = DeviceTransportMode.Remote,
        RemoteBaseUrl = "https://frame.test",
        ApiKey = "k"
    };

    private static DeviceTarget Target() => new("d1", "android", "serial", "pkg");

    private static RemoteDeviceConnection Connection(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHttpMessageHandler(respond)), Config(), Target());

    [Fact]
    public async Task ShellAsync_Success_MapsBodyToProcessResult() {
        HttpRequestMessage? seen = null;
        string? body = null;
        var conn = Connection(req => {
            seen = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"exit\":3,\"stdout\":\"out\",\"stderr\":\"err\"}");
        });

        var result = await conn.ShellAsync("ls -la", CancellationToken.None);

        Assert.Equal(new ProcessResult(3, "out", "err"), result);
        Assert.NotNull(seen);
        Assert.NotNull(body);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("https://frame.test/api/devices/d1/transport/shell", seen.RequestUri!.ToString());
        Assert.Equal("k", seen.Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("\"cmd\"", body);
    }

    [Fact]
    public async Task ShellAsync_NonSuccessStatus_ReturnsFailedResultNotThrow() {
        var conn = Connection(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await conn.ShellAsync("ls", CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("500", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellAsync_TransportThrows_ReturnsFailedResultNotThrow() {
        var conn = Connection(_ => throw new HttpRequestException("connection refused"));

        var result = await conn.ShellAsync("ls", CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("connection refused", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PullBytesAsync_Success_ReturnsBytes() {
        byte[] payload = [1, 2, 3, 4];
        var conn = Connection(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        var result = await conn.PullBytesAsync("/sdcard/file.bin", CancellationToken.None);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task PullBytesAsync_NotFound_ReturnsNull() {
        var conn = Connection(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await conn.PullBytesAsync("/sdcard/missing.bin", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task PullBytesAsync_ServerError_ReturnsNull() {
        var conn = Connection(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await conn.PullBytesAsync("/sdcard/file.bin", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task PushFileAsync_Success_PostsBase64AndReturnsTrue() {
        using var tmp = new TempDir();
        byte[] payload = [5, 6, 7];
        string local = tmp.Combine("payload.bin");
        await File.WriteAllBytesAsync(local, payload);
        string? body = null;
        var conn = Connection(req => {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        bool ok = await conn.PushFileAsync(local, "/sdcard/payload.bin", CancellationToken.None);

        Assert.True(ok);
        Assert.NotNull(body);
        Assert.Contains(Convert.ToBase64String(payload), body);
        Assert.Contains("/sdcard/payload.bin", body);
    }

    [Fact]
    public async Task PushFileAsync_NonSuccessStatus_ReturnsFalse() {
        using var tmp = new TempDir();
        string local = tmp.Combine("payload.bin");
        await File.WriteAllBytesAsync(local, [1]);
        var conn = Connection(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        bool ok = await conn.PushFileAsync(local, "/sdcard/payload.bin", CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public async Task PushFileAsync_MissingLocalFile_ReturnsFalseWithoutSendingRequest() {
        bool called = false;
        var conn = Connection(_ => {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");

        bool ok = await conn.PushFileAsync(missing, "/sdcard/payload.bin", CancellationToken.None);

        Assert.False(ok);
        Assert.False(called);
    }
}
