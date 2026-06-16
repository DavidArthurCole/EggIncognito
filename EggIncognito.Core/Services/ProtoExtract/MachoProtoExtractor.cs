namespace EggIncognito.Services.ProtoExtract;

// iOS-facing entry point for proto extraction. The decrypted Mach-O embeds serialized FileDescriptorProto
// blobs; the format-agnostic DescriptorProtoCarver does the work. Kept as a named seam (CLI, IosRunner,
// tests) and for the result-shape the callers expect. STATIC binary read; the binary is never executed.
// Proven across iOS 1.6.3 (2017) .. 1.35.8.
public static class MachoProtoExtractor
{
    public sealed record CarvedDescriptor(string Name, int FileOffset, byte[] Bytes);
    public sealed record ExtractResult(bool Ok, string? Proto, string Diagnostics);

    public static IReadOnlyList<CarvedDescriptor> CarveAll(byte[] macho) =>
        DescriptorProtoCarver.CarveAll(macho)
            .Select(c => new CarvedDescriptor(c.Name, c.FileOffset, c.Bytes)).ToList();

    public static string? EmitProto(byte[] fileDescriptorProtoBytes) =>
        DescriptorProtoCarver.EmitProto(fileDescriptorProtoBytes);

    public static ExtractResult Extract(byte[] macho)
    {
        var r = DescriptorProtoCarver.Extract(macho);
        return new ExtractResult(r.Ok, r.Proto, r.Diagnostics);
    }
}
