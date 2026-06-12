namespace EggIncognito.Capture;

// First-request parse for the front door: method, target authority, Proxy-Authorization, and the
// raw bytes consumed so the tunnel can replay them verbatim to the inner proxy.
public sealed record ProxyFirstRequest(
    string Method, string TargetHost, int TargetPort, string? ProxyAuthBasic, byte[] RawBytes);

public static class ProxyRequestParser
{
    // Reads from the buffer up to the end of headers (\r\n\r\n). Returns null if incomplete.
    public static ProxyFirstRequest? TryParse(ReadOnlySpan<byte> buffer)
    {
        var end = IndexOfHeaderEnd(buffer);
        if (end < 0) return null;
        var text = System.Text.Encoding.ASCII.GetString(buffer[..end]);
        var lines = text.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 3) return null;
        var method = parts[0];
        string host;
        int port;
        if (method == "CONNECT")
        {
            var authority = parts[1].Split(':');
            host = authority[0];
            port = authority.Length > 1 && int.TryParse(authority[1], out var p) ? p : 443;
        }
        else if (Uri.TryCreate(parts[1], UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port;
        }
        else return null;

        string? auth = null;
        foreach (var line in lines.Skip(1))
        {
            if (!line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase)) continue;
            var v = line[(line.IndexOf(':') + 1)..].Trim();
            if (v.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) auth = v[6..].Trim();
        }
        return new ProxyFirstRequest(method, host, port, auth, buffer[..(end + 4)].ToArray());
    }

    public static (string User, string Pass)? DecodeBasic(string b64)
    {
        try
        {
            var s = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var i = s.IndexOf(':');
            return i < 0 ? null : (s[..i], s[(i + 1)..]);
        }
        catch (FormatException) { return null; }
    }

    private static int IndexOfHeaderEnd(ReadOnlySpan<byte> b)
    {
        for (var i = 0; i + 3 < b.Length; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }
}
