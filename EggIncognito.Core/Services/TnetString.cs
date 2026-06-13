using System.Text;

namespace EggIncognito.Services;

// Decoder for the tnetstring serialization mitmproxy uses in its .mitm flow files. One value is
// LENGTH:PAYLOAD<tag>, where tag selects the type. Containers (list, dict) hold concatenated
// tnetstrings inside their payload. Only the read path is implemented; the importer never writes
// .mitm. Bytes/str both decode to byte[] (the caller knows which fields are text).
//
// Tags: ',' bytes  ';' str  '#' int  '^' float  '!' bool  '~' null  ']' list  '}' dict.
public static class TnetString
{
    // Decode the single tnetstring at offset. Returns the value and the offset just past its tag.
    public static (object? value, int next) Decode(byte[] data, int offset)
    {
        var colon = Array.IndexOf(data, (byte)':', offset);
        if (colon < 0) throw new FormatException("tnetstring: missing length delimiter");

        var len = ParseLength(data, offset, colon);
        var payloadStart = colon + 1;
        var tagIndex = payloadStart + len;
        if (tagIndex >= data.Length) throw new FormatException("tnetstring: payload exceeds buffer");

        var tag = (char)data[tagIndex];
        var next = tagIndex + 1;
        var payload = new ReadOnlySpan<byte>(data, payloadStart, len);

        return tag switch
        {
            ',' or ';' => (payload.ToArray(), next),
            '#' => (long.Parse(Encoding.ASCII.GetString(payload)), next),
            '^' => (double.Parse(Encoding.ASCII.GetString(payload),
                        System.Globalization.CultureInfo.InvariantCulture), next),
            '!' => (Encoding.ASCII.GetString(payload) == "true", next),
            '~' => ((object?)null, next),
            ']' => (DecodeList(data, payloadStart, len), next),
            '}' => (DecodeDict(data, payloadStart, len), next),
            _ => throw new FormatException($"tnetstring: unknown tag '{tag}'"),
        };
    }

    private static List<object?> DecodeList(byte[] data, int start, int len)
    {
        var items = new List<object?>();
        var end = start + len;
        var pos = start;
        while (pos < end)
        {
            var (value, next) = Decode(data, pos);
            items.Add(value);
            pos = next;
        }
        return items;
    }

    // Dict keys are tnetstrings (bytes/str); decode each to a UTF-8 string. Values keep their type.
    private static Dictionary<string, object?> DecodeDict(byte[] data, int start, int len)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        var end = start + len;
        var pos = start;
        while (pos < end)
        {
            var (keyObj, afterKey) = Decode(data, pos);
            var (value, afterValue) = Decode(data, afterKey);
            dict[KeyToString(keyObj)] = value;
            pos = afterValue;
        }
        return dict;
    }

    private static string KeyToString(object? key) => key switch
    {
        byte[] b => Encoding.UTF8.GetString(b),
        string s => s,
        _ => throw new FormatException("tnetstring: non-string dict key"),
    };

    private static int ParseLength(byte[] data, int start, int colon)
    {
        var len = 0;
        for (var i = start; i < colon; i++)
        {
            var d = data[i] - '0';
            if (d is < 0 or > 9) throw new FormatException("tnetstring: non-digit in length");
            len = len * 10 + d;
        }
        return len;
    }
}
