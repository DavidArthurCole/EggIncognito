namespace EggIncognito.Runner.Extract;

// Turns a pulled arm-split APK into cleaned ei.proto bytes. Test seam: inject a fake to avoid
// python/pbtk/device. sha256 of the output is the protoSha compared against the frozen ei.proto.
public interface IProtoExtractor
{
    byte[] Extract(string apkPath);
}
