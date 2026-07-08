using EggIncognito.Services.Backfill;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// The heavy extract (APKPure download + C# carve) is integration-only; here only the gate + binding are asserted.
public class ApkExtractGateTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ApkExtractService Service(IConfiguration config)
    {
        var sc = new ServiceCollection();
        var sp = sc.BuildServiceProvider();
        var apkPure = new ApkPureSource(new EmptyHttpFactory(), NullLogger<ApkPureSource>.Instance);
        return new ApkExtractService(
            config, apkPure, sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApkExtractService>.Instance);
    }

    private sealed class EmptyHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task Disabled_Throws_ExtractNotConfigured()
    {
        var svc = Service(Config(new() { ["ProtoExtract:Enabled"] = "false" }));
        await Assert.ThrowsAsync<ExtractNotConfiguredException>(() => svc.ExtractAsync("1.0.0"));
    }

    [Fact]
    public async Task Unset_Throws_ExtractNotConfigured()
    {
        var svc = Service(Config(new()));
        await Assert.ThrowsAsync<ExtractNotConfiguredException>(() => svc.ExtractAsync("1.0.0"));
    }

    [Fact]
    public void Bind_Reads_Enabled()
    {
        var opts = ApkExtractService.Bind(Config(new() { ["ProtoExtract:Enabled"] = "true" }));
        Assert.True(opts.Enabled);
        Assert.True(opts.IsConfigured);
    }

    [Fact]
    public void IsConfigured_False_When_Disabled()
    {
        var opts = ApkExtractService.Bind(Config(new() { ["ProtoExtract:Enabled"] = "false" }));
        Assert.False(opts.IsConfigured);
    }
}
