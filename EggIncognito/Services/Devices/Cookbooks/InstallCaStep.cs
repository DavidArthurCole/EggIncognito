using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallCaStep(
    IEnumerable<IDeviceCaInstaller> installers,
    IConfiguration configuration) : CookbookStep {
    public override string Id => DeviceCookbookIds.InstallCa;
    public override string Title => "Install capture CA";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (Installer(target.Platform) is null)
            return Task.FromResult(CookbookStepAvailability.No($"no ca installer for platform '{target.Platform}'"));

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath)) {
            return Task.FromResult(CookbookStepAvailability.No(
                $"no capture CA at {caPath}; run capture once so one gets minted"));
        }

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (Installer(target.Platform) is not { } installer)
            return Skipped(lines, $"no ca installer for platform '{target.Platform}'");

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath))
            return Failed(lines, $"no capture CA at {caPath}; run capture once so one gets minted");

        Add($"installing {Path.GetFileName(caPath)} on {target.Id}");
        (bool ok, string? note) = await installer.InstallAsync(target, caPath, ct);
        if (!ok) return Failed(lines, note ?? "ca install failed");

        Add(note ?? "ca installed");
        return Ok(lines, note);
    }

    private IDeviceCaInstaller? Installer(string platform) =>
        installers.FirstOrDefault(i => Platforms.Matches(i.Platform, platform));
}
