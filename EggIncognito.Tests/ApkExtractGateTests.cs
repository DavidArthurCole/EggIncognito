using EggIncognito.Services.Backfill;
using EggIncognito.Services.Backfill.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

// ApkExtractService gate + config binding, DB-free. The heavy extract (APK download + pbtk) is
// integration-only; here only the disabled-throws gate and the ProtoExtract binding are asserted.
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
    public async Task Enabled_But_Missing_Paths_Throws()
    {
        // Enabled alone is not configured: half-configured hosts are treated as not configured.
        var svc = Service(Config(new() { ["ProtoExtract:Enabled"] = "true" }));
        await Assert.ThrowsAsync<ExtractNotConfiguredException>(() => svc.ExtractAsync("1.0.0"));
    }

    [Fact]
    public void Bind_Reads_All_Fields()
    {
        var opts = ApkExtractService.Bind(Config(new()
        {
            ["ProtoExtract:Enabled"] = "true",
            ["ProtoExtract:PythonPath"] = "/usr/bin/python3",
            ["ProtoExtract:RepoPath"] = "/opt/pbtk",
        }));
        Assert.True(opts.Enabled);
        Assert.Equal("/usr/bin/python3", opts.PythonPath);
        Assert.Equal("/opt/pbtk", opts.RepoPath);
        Assert.True(opts.IsConfigured);
    }

    [Fact]
    public void IsConfigured_False_When_Enabled_But_No_Paths()
    {
        var opts = ApkExtractService.Bind(Config(new() { ["ProtoExtract:Enabled"] = "true" }));
        Assert.False(opts.IsConfigured);
    }

    [Fact]
    public void IsConfigured_False_When_Paths_But_Disabled()
    {
        var opts = ApkExtractService.Bind(Config(new()
        {
            ["ProtoExtract:Enabled"] = "false",
            ["ProtoExtract:PythonPath"] = "/usr/bin/python3",
            ["ProtoExtract:RepoPath"] = "/opt/pbtk",
        }));
        Assert.False(opts.IsConfigured);
    }
}
