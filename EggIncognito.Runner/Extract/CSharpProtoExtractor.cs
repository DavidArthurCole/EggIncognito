using System.Text;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

public sealed class CSharpProtoExtractor : IProtoExtractor
{
    public ProtoExtraction Extract(string apkPath)
    {
        var bytes = File.ReadAllBytes(apkPath);
        var r = AndroidProtoExtractor.Extract(bytes);
        if (!r.Ok || r.Proto is null)
            throw new InvalidOperationException($"android proto extract failed: {r.Diagnostics}");
        return new ProtoExtraction(Encoding.UTF8.GetBytes(r.Proto), r.ProtoSha ?? "");
    }
}
