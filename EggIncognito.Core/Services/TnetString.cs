using System.Globalization;
using System.Text;

namespace EggIncognito.Core.Services;

public static class TnetString {
    public static (object? value, int next) Decode(byte[] data, int offset) {
        int colon = Array.IndexOf(data, (byte)':', offset);
        if (colon < 0) throw new FormatException("tnetstring: missing length delimiter");

        int len = ParseLength(data, offset, colon);
        int payloadStart = colon + 1;
        int tagIndex = payloadStart + len;
        if (tagIndex >= data.Length) throw new FormatException("tnetstring: payload exceeds buffer");

        char tag = (char)data[tagIndex];
        int next = tagIndex + 1;
        var payload = new ReadOnlySpan<byte>(data, payloadStart, len);

        return tag switch {
            ',' or ';' => (payload.ToArray(), next),
            '#' => (long.Parse(Encoding.ASCII.GetString(payload), CultureInfo.InvariantCulture), next),
            '^' => (double.Parse(Encoding.ASCII.GetString(payload),
                CultureInfo.InvariantCulture), next),
            '!' => (Encoding.ASCII.GetString(payload) == "true", next),
            '~' => ((object?)null, next),
            ']' => (DecodeList(data, payloadStart, len), next),
            '}' => (DecodeDict(data, payloadStart, len), next),
            _ => throw new FormatException($"tnetstring: unknown tag '{tag}'")
        };
    }

    private static List<object?> DecodeList(byte[] data, int start, int len) {
        var items = new List<object?>();
        int end = start + len;
        int pos = start;
        while (pos < end) {
            (object? value, int next) = Decode(data, pos);
            items.Add(value);
            pos = next;
        }

        return items;
    }


    private static Dictionary<string, object?> DecodeDict(byte[] data, int start, int len) {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        int end = start + len;
        int pos = start;
        while (pos < end) {
            (object? keyObj, int afterKey) = Decode(data, pos);
            (object? value, int afterValue) = Decode(data, afterKey);
            dict[KeyToString(keyObj)] = value;
            pos = afterValue;
        }

        return dict;
    }

    private static string KeyToString(object? key) => key switch {
        byte[] b => Encoding.UTF8.GetString(b),
        string s => s,
        _ => throw new FormatException("tnetstring: non-string dict key")
    };

    private static int ParseLength(byte[] data, int start, int colon) {
        int len = 0;
        for (int i = start; i < colon; i++) {
            int d = data[i] - '0';
            if (d is < 0 or > 9) throw new FormatException("tnetstring: non-digit in length");
            len = len * 10 + d;
        }

        return len;
    }
}
