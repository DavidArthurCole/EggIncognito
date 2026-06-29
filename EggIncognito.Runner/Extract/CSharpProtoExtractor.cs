using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

// Carves cleaned ei.proto bytes from an arm-split APK in-process (pure C#), replacing the old pbtk python
// shell-out. Reads the file the caller already staged to disk, hands the bytes to AndroidProtoExtractor.
public sealed class CSharpProtoExtractor : IProtoExtractor
{
    public byte[] Extract(string apkPath)
    {
        var bytes = File.ReadAllBytes(apkPath);
        return Encoding.UTF8.GetBytes(AndroidProtoExtractor.ExtractProtoText(bytes));
    }
}
