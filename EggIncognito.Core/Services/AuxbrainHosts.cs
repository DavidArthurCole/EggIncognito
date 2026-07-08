// Single source of truth for "is this an Egg, Inc. auxbrain host". Two consumers:
//   - InspectorApiController, which also allows localhost as a /send target.
//   - the capture proxy, which decrypts only auxbrain hosts and passes everything else through.
// Keeping the rule here means the allowlist and the proxy filter can never drift apart.

namespace EggIncognito.Services;

public static class AuxbrainHosts
{
    // Suffixes that identify auxbrain traffic.
    private static readonly string[] Suffixes =
    [
        "auxbrain.com",
        "auxbrainhome.appspot.com",
    ];

    /// <summary>True for *.auxbrain.com, auxbrainhome.appspot.com, and its Google App Engine
    /// "&lt;service&gt;-dot-auxbrainhome.appspot.com" subdomains. Rejects look-alikes such as
    /// "auxbrainhome.appspot.com.evil.com". Accepts an authority too (a ":port" suffix or IPv6 brackets
    /// are normalized away), so callers can pass uri.Host or a CONNECT target directly.</summary>
    public static bool IsAuxbrain(string host)
    {
        host = NormalizeHost(host);
        if (host.Length == 0) return false;
        foreach (var s in Suffixes)
        {
            // Exact host, or a real DNS subdomain.
            if (host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
                return true;

            // Google App Engine service hosts: "<service>-dot-<suffix>". The service label must be a
            // single DNS label: no dot, and no leading/trailing hyphen.
            var marker = "-dot-" + s;
            if (host.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                var service = host[..^marker.Length];
                if (service.Length > 0 && !service.Contains('.') &&
                    service[0] != '-' && service[^1] != '-' &&
                    service.All(c => char.IsLetterOrDigit(c) || c == '-'))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Reduce an authority to its bare host: "host:port" -> "host", "[v6]" / "[v6]:port" ->
    /// "v6". Anything with a '/' or malformed (unclosed bracket, multiple unbracketed colons) normalizes
    /// to "" (never matches).</summary>
    public static string NormalizeHost(string authority)
    {
        if (string.IsNullOrEmpty(authority) || authority.Contains('/')) return "";

        // Bracketed IPv6: the host is the bracket contents; an optional :port follows the bracket.
        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']');
            return close > 1 ? authority[1..close] : "";
        }

        var colon = authority.IndexOf(':');
        if (colon < 0) return authority;
        // More than one colon without brackets is a raw IPv6 literal or garbage; reject either way
        // (an IP can never be an auxbrain host).
        if (authority.IndexOf(':', colon + 1) >= 0) return "";
        return authority[..colon];
    }
}
