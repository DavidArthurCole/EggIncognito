using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

public sealed class CSharpProtoExtractor : IProtoExtractor
{
    public byte[] Extract(string apkPath)
    {
        var bytes = File.ReadAllBytes(apkPath);
        return Encoding.UTF8.GetBytes(AndroidProtoExtractor.ExtractProtoText(bytes));
    }
}
