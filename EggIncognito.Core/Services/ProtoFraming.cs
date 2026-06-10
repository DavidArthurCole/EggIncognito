// Shared low-level framing/codec helpers for the wire format, so the capture pipeline, the dashboard
// decoder, the Transport Inspector, and the extraction pipeline all decode the wire identically:
// tolerant base64, tolerant decompression (gzip/zlib/deflate), and AuthenticatedMessage unwrap.

using System.IO.Compression;
using Google.Protobuf;

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

    // Decompress a payload, auto-detecting gzip / zlib / raw deflate. Returns the bytes unchanged if
    // none apply (a Compressed flag may be set on already-plain proto).
    public static byte[] Decompress(byte[] compressed)
    {
        // GZip: 1f 8b header
        if (compressed.Length >= 2 && compressed[0] == 0x1f && compressed[1] == 0x8b)
        {
            using var i = new MemoryStream(compressed);
            using var gz = new GZipStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); gz.CopyTo(o); return o.ToArray();
        }
        // ZLib: try first (has 2-byte header)
        try
        {
            using var i = new MemoryStream(compressed);
            using var zl = new ZLibStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); zl.CopyTo(o); return o.ToArray();
        }
        catch (InvalidDataException) { }
        // Raw Deflate: no header
        try
        {
            using var i = new MemoryStream(compressed);
            using var df = new DeflateStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream(); df.CopyTo(o); return o.ToArray();
        }
        catch (InvalidDataException) { }
        // Return raw - Compressed flag may be set but bytes are uncompressed proto
        return compressed;
    }

    // Unwrap an AuthenticatedMessage payload (decompressing if needed). Throws if not wrapped.
    public static byte[] Unwrap(byte[] bytes)
    {
        var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
        return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
    }

    // Best-effort unwrap: returns null if the bytes are not an AuthenticatedMessage with a payload.
    public static byte[]? TryUnwrap(byte[] bytes)
    {
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
            if (outer.Message.Length == 0) return null;
            return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
        }
        catch (InvalidProtocolBufferException) { return null; }
    }
}
