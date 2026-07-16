namespace EggIncognito.Services;


public static class BlobDecoder
{
    public sealed record DecodeResult(string? Type, string? Json, bool Wrapped, int Confidence);

    public static DecodeResult Decode(string base64)
    {
        byte[] bytes;
        try { bytes = ProtoFraming.FromBase64Loose(base64); }
        catch { return new(null, null, false, 0); }

        var raw = EndpointExtractor.AutoDetect(bytes);
        var unwrappedBytes = ProtoFraming.TryUnwrap(bytes);
        var unw = unwrappedBytes is null
            ? default((string? typeName, string? json, int confidence, int bestScore, int secondBestScore))
            : EndpointExtractor.AutoDetect(unwrappedBytes);

        var useUnwrapped = unwrappedBytes is not null && unw.bestScore > raw.bestScore;
        var chosen = useUnwrapped ? unw : raw;
        if (chosen.typeName is null || chosen.json is null) return new(null, null, useUnwrapped, 0);
        return new(chosen.typeName, chosen.json, useUnwrapped, chosen.confidence);
    }
}
