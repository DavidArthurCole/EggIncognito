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
    /// "auxbrainhome.appspot.com.evil.com" and "evil.com-dot-auxbrainhome.appspot.com".</summary>
    public static bool IsAuxbrain(string host)
    {
        foreach (var s in Suffixes)
        {
            // Exact host, or a real DNS subdomain.
            if (host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
                return true;

            // Google App Engine service hosts: "<service>-dot-<suffix>". The service label must be a
            // single DNS label with no dot.
            var marker = "-dot-" + s;
            if (host.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                var service = host[..^marker.Length];
                if (service.Length > 0 && !service.Contains('.') &&
                    service.All(c => char.IsLetterOrDigit(c) || c == '-'))
                    return true;
            }
        }
        return false;
    }
}
