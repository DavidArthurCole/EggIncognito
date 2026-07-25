using System.Globalization;
using System.Text;

namespace EggIncognito.Capture;

public sealed record ProxyFirstRequest(string Method, string TargetHost, int TargetPort, byte[] RawBytes);

public static class ProxyRequestParser {
    public static ProxyFirstRequest? TryParse(ReadOnlySpan<byte> buffer) {
        int end = IndexOfHeaderEnd(buffer);
        if (end < 0) return null;
        string text = Encoding.ASCII.GetString(buffer[..end]);
        string[] lines = text.Split("\r\n");
        string[] parts = lines[0].Split(' ');
        if (parts.Length < 3) return null;
        string method = parts[0];
        string host;
        int port;
        if (method == "CONNECT") {
            string[] authority = parts[1].Split(':');
            host = authority[0];
            port = authority.Length > 1 &&
                   int.TryParse(authority[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
                ? p
                : 443;
        } else if (Uri.TryCreate(parts[1], UriKind.Absolute, out var uri)) {
            host = uri.Host;
            port = uri.Port;
        } else {
            return null;
        }

        return new ProxyFirstRequest(method, host, port, buffer[..(end + 4)].ToArray());
    }

    private static int IndexOfHeaderEnd(ReadOnlySpan<byte> b) {
        for (int i = 0; i + 3 < b.Length; i++) {
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n')
                return i;
        }

        return -1;
    }
}
