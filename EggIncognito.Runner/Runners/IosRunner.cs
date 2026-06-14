namespace EggIncognito.Runner.Runners;

// iOS proto extraction (Mach-O, not an APK with libegginc.so) has no proven recipe yet. This seam keeps
// the platform shape so the ios slave device is a defined extension, not a rewrite. It throws until the
// ios extractor lands in its own spec.
public sealed class IosRunner : IDeviceRunner
{
    public string Platform => "ios";

    public RunOutcome RunOnce(bool force) =>
        throw new NotSupportedException(
            "ios proto extraction is not implemented yet; no Mach-O extraction recipe exists. "
            + "Run the android platform, or implement the ios extractor first.");
}
