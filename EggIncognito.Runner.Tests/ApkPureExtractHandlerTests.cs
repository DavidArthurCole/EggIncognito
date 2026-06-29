using EggIncognito.Core.Models;
using EggIncognito.Runner.Extract;
using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Runner.Tests;

// Gate tests: 401/400 short-circuit before download. 409 uses a blocking HttpMessageHandler so the first
// call holds the single-flight lock inside DownloadApkAsync while a second call races in. No real network.
public class ApkPureExtractHandlerTests
{
    private static ApkPureExtractHandler Make(string secret, HttpMessageHandler? handler = null) =>
        new(secret, new ApkPureDownloader(new HttpClient(handler ?? new HttpClientHandler())),
            new CSharpProtoExtractor(), new NullClientVersionReader(),
            new EggIncognito.Runner.State.ClientVersionState(
                Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}"), null),
            _ => Task.CompletedTask);

    [Fact]
    public async Task BadBearer_Is401()
    {
        var r = await Make("secret").HandleAsync("Bearer wrong", "1.35.7");
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public async Task MissingAppVersion_Is400()
    {
        var r = await Make("secret").HandleAsync("Bearer secret", "");
        Assert.Equal(400, r.Status);
    }

    [Fact]
    public async Task Concurrent_Is409()
    {
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var h = Make("secret", new BlockingHandler(entered, release));

        var first = Task.Run(() => h.HandleAsync("Bearer secret", "1.35.7"));
        entered.Wait(); // first call now inside DownloadApkAsync, holding the lock
        var second = await h.HandleAsync("Bearer secret", "1.35.7");
        Assert.Equal(409, second.Status);

        release.Set();
        await first;
    }

    // Blocks the HTTP send until released, so the caller stays inside the lock. Then returns 404 so the
    // first call resolves to 502 download-failed and completes cleanly.
    private sealed class BlockingHandler(ManualResetEventSlim entered, ManualResetEventSlim release) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.Set();
            release.Wait(cancellationToken);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
