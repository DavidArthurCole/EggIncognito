namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoProtoExtractor {
    public static IReadOnlyList<CarvedDescriptor> CarveAll(byte[] macho) =>
        DescriptorProtoCarver.CarveAll(macho)
            .Select(c => new CarvedDescriptor(c.Name, c.FileOffset, c.Bytes)).ToList();

    public static string? EmitProto(byte[] fileDescriptorProtoBytes) =>
        DescriptorProtoCarver.EmitProto(fileDescriptorProtoBytes);

    public static ExtractResult Extract(byte[] macho) {
        var r = DescriptorProtoCarver.Extract(macho);
        return new ExtractResult(r.Ok, r.Proto, r.Diagnostics, r.ProtoSha);
    }

    public sealed record CarvedDescriptor(string Name, int FileOffset, byte[] Bytes);

    public sealed record ExtractResult(bool Ok, string? Proto, string Diagnostics, string? ProtoSha = null);
}
