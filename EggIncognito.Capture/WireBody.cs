using System.Text;
using EggIncognito.Core.Services;
using Ei;

namespace EggIncognito.Capture;

public static class WireBody {
    public static (string responseB64, string shape) Normalize(byte[] respBytes) {
        string shape;
        if (respBytes.Length >= 2 && respBytes[0] == 0x1f && respBytes[1] == 0x8b) {
            respBytes = ProtoFraming.Decompress(respBytes);
            shape = "gunzipped+";
        } else {
            shape = "";
        }

        if (LooksLikeBase64Text(respBytes)) {
            string text = Encoding.ASCII.GetString(respBytes).Trim();
            if (DecodesToAuthMessage(text))
                return (text, shape + "base64-text");
        }

        return (Convert.ToBase64String(respBytes), shape + "raw");
    }

    private static bool DecodesToAuthMessage(string text) {
        try {
            byte[] bytes = ProtoFraming.FromBase64Loose(text);
            _ = AuthenticatedMessage.Parser.ParseFrom(bytes);
            return true;
        } catch {
            return false;
        }
    }

    public static bool LooksLikeBase64Text(byte[] b) {
        if (b.Length == 0) return false;
        foreach (byte c in b) {
            bool ok = c is >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z'
                or >= (byte)'0' and <= (byte)'9'
                or (byte)'+' or (byte)'/' or (byte)'='
                or (byte)'\r' or (byte)'\n' or (byte)' ';
            if (!ok) return false;
        }

        return true;
    }

    public static string? ExtractDataParam(string body) {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (string pair in body.Split('&')) {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (pair[..eq] == "data")
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
        }

        return null;
    }
}
