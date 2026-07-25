using System.IO.Compression;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services;

public static class ProtoFraming {
    public static byte[] FromBase64Loose(string s) {
        s = s.Trim().Replace(' ', '+');
        int pad = s.Length % 4;
        if (pad != 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s);
    }


    public static byte[] Decompress(byte[] compressed) {
        if (compressed.Length >= 2 && compressed[0] == 0x1f && compressed[1] == 0x8b) {
            using var i = new MemoryStream(compressed);
            using var gz = new GZipStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream();
            gz.CopyTo(o);
            return o.ToArray();
        }

        try {
            using var i = new MemoryStream(compressed);
            using var zl = new ZLibStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream();
            zl.CopyTo(o);
            return o.ToArray();
        } catch (InvalidDataException) {
        }

        try {
            using var i = new MemoryStream(compressed);
            using var df = new DeflateStream(i, CompressionMode.Decompress);
            using var o = new MemoryStream();
            df.CopyTo(o);
            return o.ToArray();
        } catch (InvalidDataException) {
        }

        return compressed;
    }


    public static byte[] Unwrap(byte[] bytes) {
        var outer = AuthenticatedMessage.Parser.ParseFrom(bytes);
        return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
    }


    public static byte[]? TryUnwrap(byte[] bytes) {
        try {
            var outer = AuthenticatedMessage.Parser.ParseFrom(bytes);
            return outer.Message.Length == 0 ? null :
                outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
        } catch (InvalidProtocolBufferException) {
            return null;
        }
    }
}
