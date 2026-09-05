using System.Text;

namespace EggIncognito.Core.Services.Devices;

public static class KeyboxCodec {
    private const int Base64Rounds = 10;

    public static string Decode(byte[] megatron) {
        string text = Encoding.ASCII.GetString(megatron);
        for (int round = 1; round <= Base64Rounds; round++) {
            try {
                text = Encoding.ASCII.GetString(Convert.FromBase64String(StripWhitespace(text)));
            } catch (FormatException ex) {
                throw new FormatException($"keybox decode failed at base64 round {round}", ex);
            }
        }

        byte[] plain;
        try {
            plain = Convert.FromHexString(StripWhitespace(text));
        } catch (FormatException ex) {
            throw new FormatException("keybox decode failed at hex stage", ex);
        }

        return Rot13(Encoding.UTF8.GetString(plain));
    }

    public static string Encode(string keyboxXml) {
        string text = Convert.ToHexStringLower(Encoding.UTF8.GetBytes(Rot13(keyboxXml)));
        for (int round = 0; round < Base64Rounds; round++) {
            text = Convert.ToBase64String(Encoding.ASCII.GetBytes(text));
        }

        return text;
    }

    public static bool LooksLikeKeybox(string xml) =>
        xml.Contains("<AndroidAttestation", StringComparison.Ordinal) || xml.Contains("<Keybox", StringComparison.Ordinal);

    private static string StripWhitespace(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string Rot13(string text) {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++) {
            char c = chars[i];
            if (c is >= 'a' and <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z') chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }

        return new string(chars);
    }
}
