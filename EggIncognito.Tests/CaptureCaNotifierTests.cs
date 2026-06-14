using System.Net;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// CA-over-Discord-DM wiring: notifier selection by config, the REST notifier's fail-closed behavior,
// and that a failed DM never breaks session start.
public class CaptureCaNotifierTests
{
    // Always reports a failure delivery, so Start's best-effort path is exercised without Discord.
    private sealed class FailingNotifier : ICaptureCaNotifier
    {
        public int Calls { get; private set; }
        public Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(false);
        }
    }

    // Resolves only the types DeliverFreshSetupAsync asks for: the notifier, and the credential store
    // (null here, the DB-free test exercises the no-store fail path).
    private sealed class StubServices(ICaptureCaNotifier notifier) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ICaptureCaNotifier) ? notifier : null;
    }

    private sealed class FakeAppMode(bool canCapture, bool hostedEnabled) : IAppMode
    {
        public AppMode Mode => AppMode.Hosted;
        public bool CanCapture => canCapture;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => hostedEnabled;
    }

    private sealed class FakeUser(bool authed, bool supporter) : ICurrentUser
    {
        public bool IsAuthenticated => authed;
        public string? DiscordId => authed ? "tester" : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(UserRole.Viewer, need);
    }

    private sealed class FakeSupporters(bool result) : ISupporterStatus
    {
        public Task<bool> CheckAsync(string discordId, CancellationToken ct = default) => Task.FromResult(result);
    }

    // Fresh-CA proxy: writes a dummy .cer to caPath at start and reports FreshCa, so the controller's
    // DM path engages.
    private sealed class FreshCaProxy : ICaptureProxy
    {
#pragma warning disable CS0067 // events required by ICaptureProxy; not fired in this stub
        public event Action<CapturedFlow>? FlowCaptured;
        public event Action<int, string?>? ClientConnected;
        public event Action<int, string?>? ClientDisconnected;
        public event Action? AuxbrainConnect;
        public event Action<string>? DecryptError;
#pragma warning restore CS0067

        public bool FreshCa => true;
        public string? RootThumbprint => "FRESH-THUMB";

        public async Task StartAsync(int port, string caPath, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caPath))!);
            await File.WriteAllBytesAsync(caPath, [0x30, 0x82, 0x01], ct);
        }

        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static CaptureSession FreshCaSession()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "egi-cadm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var opts = new CaptureSessionOptions(Port: 19090, Eid: null, Label: null,
            Overwrite: false, Verbose: false, CapturePath: tmp,
            CaPath: Path.Combine(tmp, "ca.cer"), WriteEndpoints: false);
        return new CaptureSession(CaptureSessionManagerTests.RealContentRoot(), opts, _ => new FreshCaProxy());
    }

    private static CaptureSessionManager FreshCaManager() =>
        new(HostedCaptureOptions.Defaults(), (_, _) => FreshCaSession());

    // Returns a canned response for every request, so the notifier's REST calls are deterministic.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Start_FreshCa_NoStore_Still200_AndFlagsCaDmFailed()
    {
        // No DB configured (the DB-free test path): the setup DM cannot mint a token, so delivery
        // fails fast and the session is flagged, but Start must still return 200.
        var manager = FreshCaManager();
        var notifier = new FailingNotifier();
        var controller = new CaptureController(
            manager, new FakeAppMode(canCapture: false, hostedEnabled: true),
            new FakeUser(true, supporter: true), new FakeSupporters(true),
            HostedCaptureOptions.Defaults(), new StubServices(notifier));

        var r = await controller.Start(CancellationToken.None);

        Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
        var session = manager.Get("tester");
        Assert.NotNull(session);
        Assert.True(session!.CaDmFailed);
        Assert.True(session.Status.CaDmFailed);
        await session.StopAsync();
    }

    [Fact]
    public async Task DiscordNotifier_FailingHttp_ReturnsFalse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var notifier = new DiscordCaptureCaNotifier(
            new StubHttpFactory(handler),
            Config(new() { ["Discord:BotToken"] = "token" }),
            NullLogger<DiscordCaptureCaNotifier>.Instance);

        var ok = await notifier.SendSetupAsync(Dm(), CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task DiscordNotifier_NoToken_ReturnsFalse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new DiscordCaptureCaNotifier(
            new StubHttpFactory(handler),
            Config(new()),
            NullLogger<DiscordCaptureCaNotifier>.Instance);

        var ok = await notifier.SendSetupAsync(Dm(), CancellationToken.None);
        Assert.False(ok);
    }

    private static CaptureSetupDm Dm() =>
        new("123", [0x30, 0x82, 0x01], "capture.example.com", 8443, "123", "tok-abc");

    [Fact]
    public void MobileConfig_EmbedsCert_AndIsStablePerCert()
    {
        byte[] cer = [0x30, 0x82, 0x01, 0x02, 0x03];
        var a = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer));
        var b = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer));
        Assert.Equal(a, b); // deterministic per cert
        Assert.Contains("com.apple.security.root", a);
        Assert.Contains(Convert.ToBase64String(cer), a);
        // A different cert yields a different profile UUID.
        var c = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile([0x30, 0x99]));
        Assert.NotEqual(a, c);
    }

    // Mirrors the Program.cs gate without booting the full host (which would try to log the bot in).
    private static ICaptureCaNotifier ResolveNotifier(string? botToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("discord-api");
        services.AddSingleton(Config(new() { ["Discord:BotToken"] = botToken }));
        if (!string.IsNullOrWhiteSpace(botToken))
            services.AddSingleton<ICaptureCaNotifier, DiscordCaptureCaNotifier>();
        else
            services.AddSingleton<ICaptureCaNotifier, NoopCaptureCaNotifier>();
        return services.BuildServiceProvider().GetRequiredService<ICaptureCaNotifier>();
    }

    [Fact]
    public void Notifier_NoBotToken_RegistersNoop() =>
        Assert.IsType<NoopCaptureCaNotifier>(ResolveNotifier(null));

    [Fact]
    public void Notifier_BotTokenSet_RegistersDiscord() =>
        Assert.IsType<DiscordCaptureCaNotifier>(ResolveNotifier("token"));
}
