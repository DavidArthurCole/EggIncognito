namespace EggIncognito.Services;


public static class BlobDecoder {
    public sealed record DecodeResult(string? Type, string? Json, bool Wrapped, int Confidence);

    public static DecodeResult Decode(string base64) {
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(base64); } catch { return new(null, null, false, 0); }

        var raw = EndpointExtractor.AutoDetect(bytes);
        var unwrappedBytes = ProtoFraming.TryUnwrap(bytes);
        var unw = unwrappedBytes is null
            ? default
            : EndpointExtractor.AutoDetect(unwrappedBytes);

        var useUnwrapped = unwrappedBytes is not null && unw.bestScore > raw.bestScore;
        var (typeName, json, confidence, _, _) = useUnwrapped ? unw : raw;
        return typeName is null || json is null ? new(null, null, useUnwrapped, 0) : new(typeName, json, useUnwrapped, confidence);
    }
}
