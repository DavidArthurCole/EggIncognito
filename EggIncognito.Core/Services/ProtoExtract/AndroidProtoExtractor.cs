namespace EggIncognito.Services.ProtoExtract;

// Android-facing entry point for proto extraction. The arm split's lib/arm64-v8a/libegginc.so embeds
// serialized FileDescriptorProto blobs; the format-agnostic ArchiveProtoExtractor pulls the .so out of
// the apk (a zip) and carves them. A bare .so or raw binary is carved directly. Named seam mirroring
// MachoProtoExtractor so the callers (ApkExtractService, the runner) have a clear C# replacement for the
// old pbtk python path. STATIC binary read; never executed.
public static class AndroidProtoExtractor
{
    // Accepts apk/xapk-split zip bytes OR a bare libegginc.so / raw binary. ArchiveProtoExtractor handles
    // both: it tries the zip candidate entries first, then falls back to a raw whole-buffer carve.
    public static DescriptorProtoCarver.ExtractResult Extract(byte[] apkOrSoBytes) =>
        ArchiveProtoExtractor.Extract(apkOrSoBytes);

    // Cleaned proto2 text or throw. Replaces RunPbtkAsync: ProtoCleanup.Clean already runs inside the
    // carver's Extract, so the returned Proto is the merged + aux-stripped text.
    public static string ExtractProtoText(byte[] apkOrSoBytes)
    {
        var r = Extract(apkOrSoBytes);
        if (!r.Ok || r.Proto is null)
            throw new InvalidOperationException($"android proto extract failed: {r.Diagnostics}");
        return r.Proto;
    }
}
