namespace EggIncognito.Runner.Extract;

// Turns a pulled arm-split APK into the cleaned ei.proto bytes. A seam so the runner can be tested
// with a fake that returns fixed bytes, no python/pbtk/device required. sha256 of these bytes is the
// protoSha the sync server compares against its frozen ei.proto, so the cleanup is not optional.
public interface IProtoExtractor
{
    byte[] Extract(string apkPath);
}
