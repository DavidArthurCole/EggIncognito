namespace EggIncognito.Runner.Extract;

public interface IProtoExtractor
{
    ProtoExtraction Extract(string apkPath);
}

public readonly record struct ProtoExtraction(byte[] ProtoText, string ProtoSha);
