// EggIncognito.Core/Services/ProtoFraming.cs
//
// Shared low-level framing/codec helpers for the wire format, so the capture pipeline,
// the dashboard decoder, and the Transport Inspector all decode base64 the same way.
// Decompression lives on EndpointExtractor (gzip/zlib/deflate tolerant); this is the
// home for the tolerant base64 decode that used to be duplicated across consumers.

namespace EggIncognito.Services;

public static class ProtoFraming
{
    // Tolerant base64 decode: form-decoding can turn '+' into ' ' and strip padding. Restore both
    // so a valid proto blob is not silently dropped. Strictly more permissive than
    // Convert.FromBase64String - any input that decoded before still decodes identically.
    public static byte[] FromBase64Loose(string s)
    {
        s = s.Trim().Replace(' ', '+');
        var pad = s.Length % 4;
        if (pad != 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s);
    }
}
