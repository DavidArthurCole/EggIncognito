using System.Text;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// Pure wire-body helpers for the capture proxy's response/request handlers, factored out of
// UnobtaniumCaptureProxy so the three-shape normalization can be unit-tested without a live proxy.
public static class WireBody
{
    // Normalize a decrypted response body to the canonical responseB64 the endpoint pipeline +
    // decoder expect: base64 of the AuthenticatedMessage bytes. The wire body arrives in THREE
    // shapes depending on the endpoint and the client's Accept-Encoding:
    //   1. base64 TEXT (the API's normal framing)            -> use the text as-is
    //   2. gzip of the AuthenticatedMessage (real device)    -> gunzip, then (text-or-base64)
    //   3. raw AuthenticatedMessage bytes                    -> base64 directly
    // `shape` is a short human label for the trace log.
    public static (string responseB64, string shape) Normalize(byte[] respBytes)
    {
        // Unwrap transport gzip first (the real device gets gzip-compressed responses; body starts
        // 1f 8b). The DECOMPRESSED payload is what the API actually framed.
        string shape;
        if (respBytes.Length >= 2 && respBytes[0] == 0x1f && respBytes[1] == 0x8b)
        {
            respBytes = EndpointExtractor.Decompress(respBytes);
            shape = "gunzipped+";
        }
        else shape = "";

        // The framed payload is itself base64 TEXT of the AuthenticatedMessage (the API's normal
        // framing). Use it as-is; only base64-encode if it is somehow raw bytes.
        if (LooksLikeBase64Text(respBytes))
            return (Encoding.ASCII.GetString(respBytes).Trim(), shape + "base64-text");
        return (Convert.ToBase64String(respBytes), shape + "raw");
    }

    // True if the bytes are entirely the base64 alphabet (+ whitespace/padding) - i.e. the body is
    // base64 TEXT, not raw binary. A raw gzip/proto body contains bytes outside this set.
    public static bool LooksLikeBase64Text(byte[] b)
    {
        if (b.Length == 0) return false;
        foreach (var c in b)
        {
            bool ok = c is (>= (byte)'A' and <= (byte)'Z')
                or (>= (byte)'a' and <= (byte)'z')
                or (>= (byte)'0' and <= (byte)'9')
                or (byte)'+' or (byte)'/' or (byte)'=' // base64
                or (byte)'\r' or (byte)'\n' or (byte)' '; // whitespace
            if (!ok) return false;
        }
        return true;
    }

    // Pull the base64 value of the `data` form field out of a urlencoded body.
    public static string? ExtractDataParam(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (var pair in body.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (pair[..eq] == "data")
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
        }
        return null;
    }
}
