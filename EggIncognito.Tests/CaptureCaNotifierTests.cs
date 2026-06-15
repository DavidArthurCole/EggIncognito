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
        new("123", [0x30, 0x82, 0x01], "[2a01:4f8:c012:e15b::5]", 8443);

    [Fact]
    public void BuildMessage_ShowsProxyAddress_NoCredentials()
    {
        var msg = DiscordCaptureCaNotifier.BuildMessage(
            new CaptureSetupDm("123", [0x30, 0x82, 0x01], "2a01:4f8:c012:e15b::5", 8443));

        Assert.Contains("2a01:4f8:c012:e15b::5", msg); // bare address, no brackets
        Assert.Contains("8443", msg);
        Assert.Contains("Auth off", msg);
        // No credential lines remain.
        Assert.DoesNotContain("Token", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", msg);
        Assert.DoesNotContain("password", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileConfig_EmbedsCert_AndIsStablePerUser()
    {
        byte[] cer = [0x30, 0x82, 0x01, 0x02, 0x03];
        var a = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        var b = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        Assert.Equal(a, b);
        Assert.Contains("com.apple.security.root", a);
        Assert.Contains(Convert.ToBase64String(cer), a);
    }

    // The profile identity is anchored to the user, not the cert, so a regenerated cert reinstalls into
    // the SAME profile (iOS replaces) instead of stacking a new one. The new cert bytes still embed.
    [Fact]
    public void MobileConfig_SameUser_DifferentCert_KeepsProfileIdentity()
    {
        var newCert = Convert.ToBase64String([(byte)0x30, 0x99]);
        var a = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile([0x30, 0x82, 0x01], "user-1"));
        var b = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile([0x30, 0x99], "user-1"));
        Assert.Equal(ProfileUuid(a), ProfileUuid(b)); // same user -> same profile UUID (replace, not stack)
        Assert.Contains(newCert, b); // the new cert bytes are embedded
    }

    [Fact]
    public void MobileConfig_DifferentUsers_GetDistinctProfiles()
    {
        byte[] cer = [0x30, 0x82, 0x01];
        var a = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        var b = System.Text.Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-2"));
        Assert.NotEqual(ProfileUuid(a), ProfileUuid(b));
    }

    // The top-level profile PayloadUUID is the last PayloadUUID in the plist (after the cert payload's).
    private static string ProfileUuid(string plist) =>
        plist.Split("<key>PayloadUUID</key>")[^1].Split("<string>")[1].Split("</string>")[0];

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
