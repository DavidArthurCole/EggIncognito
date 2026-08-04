namespace EggIncognito.Services.ProtoExtract;

public static class ProtoCanonicalForm {
    public static NormalizeResult Normalize(string protoText) {
        try {
            var first = ProtoTextCompiler.Compile(protoText);
            string text1 = DescriptorProtoCarver.EmitProto(first);
            var second = ProtoTextCompiler.Compile(text1);
            string text2 = DescriptorProtoCarver.EmitProto(second);
            if (!string.Equals(text1, text2, StringComparison.Ordinal)) {
                return new NormalizeResult(false, null, null, "fixpoint mismatch");
            }

            return new NormalizeResult(true, text1, EggIncognito.Core.ProtoHash.OfDescriptor(second), null);
        } catch (FormatException ex) {
            return new NormalizeResult(false, null, null, ex.Message);
        }
    }

    public sealed record NormalizeResult(bool Ok, string? Text, string? Sha, string? Error);
}
