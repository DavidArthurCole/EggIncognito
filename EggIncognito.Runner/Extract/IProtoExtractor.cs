namespace EggIncognito.Runner.Extract;

// sha256 of the output is the protoSha compared against the frozen ei.proto.
public interface IProtoExtractor
{
    byte[] Extract(string apkPath);
}
