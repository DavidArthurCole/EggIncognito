namespace EggIncognito.Core.Services.ProtoExtract;

public static class AndroidProtoExtractor {
    public static DescriptorProtoCarver.ExtractResult Extract(byte[] apkOrSoBytes) =>
        ArchiveProtoExtractor.Extract(apkOrSoBytes);


    public static string ExtractProtoText(byte[] apkOrSoBytes) {
        var r = Extract(apkOrSoBytes);
        return !r.Ok || r.Proto is null
            ? throw new InvalidOperationException($"android proto extract failed: {r.Diagnostics}")
            : r.Proto;
    }
}
