using EggIncognito.Bot;
using Microsoft.Extensions.Logging.Abstractions;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace EggIncognito.Tests;

public class EggIncognitoBotHostedServiceTests {
    [Fact]
    public async Task StartAsync_EmptyToken_DoesNotThrow() {
        var cfg = new BotConfig { Name = "EggIncognito", Token = "", Build = new VerifyInfo() };
        var svc = new EggIncognitoBotHostedService(cfg, NullLogger<EggIncognitoBotHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);


    }
}
