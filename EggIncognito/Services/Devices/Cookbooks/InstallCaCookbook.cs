using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallCaCookbook(
    IEnumerable<IDeviceCaInstaller> installers,
    IConfiguration configuration) : IDeviceCookbook {
    public string Id => DeviceCookbookIds.InstallCa;
    public string Title => "Install capture CA";
    public string Summary => "Trusts the EggIncognito capture root CA on the device so the proxy can decrypt auxbrain.";

    public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (Installer(target.Platform) is null)
            return Task.FromResult(Unavailable($"no ca installer for platform '{target.Platform}'"));

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath))
            return Task.FromResult(Unavailable($"no capture CA at {caPath}; run capture once so one gets minted"));

        return Task.FromResult(new DeviceCookbookInfo(Id, Title, Summary, true));
    }

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);
        var target = context.Target;

        if (Installer(target.Platform) is not { } installer)
            return log.Fail(Id, "installer", $"no ca installer for platform '{target.Platform}'");

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath))
            return log.Fail(Id, "ca-file", $"no capture CA at {caPath}; run capture once so one gets minted");

        log.Add($"installing {Path.GetFileName(caPath)} on {target.Id}");
        (bool ok, string? note) = await installer.InstallAsync(target, caPath, ct);
        if (!ok) return log.Fail(Id, "install", note ?? "ca install failed");

        log.Add(note ?? "ca installed");
        return log.Ok(Id, note);
    }

    private IDeviceCaInstaller? Installer(string platform) =>
        installers.FirstOrDefault(i => Platforms.Matches(i.Platform, platform));

    private DeviceCookbookInfo Unavailable(string reason) => new(Id, Title, Summary, false, reason);
}
