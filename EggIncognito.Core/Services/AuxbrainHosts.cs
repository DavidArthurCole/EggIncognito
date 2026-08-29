namespace EggIncognito.Core.Services;

public static class AuxbrainHosts {
    public const string Origin = "https://www.auxbrain.com";
    public const string ContextOrigin = "https://ctx-dot-auxbrainhome.appspot.com";

    private static readonly string[] ContextPrefixes = ["ei_ctx", "ei_srv"];

    private static readonly string[] Suffixes = [
        "auxbrain.com",
        "auxbrainhome.appspot.com"
    ];


    public static string OriginForPath(string? path) {
        if (string.IsNullOrEmpty(path)) return Origin;
        string trimmed = path.TrimStart('/');
        foreach (string prefix in ContextPrefixes) {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return ContextOrigin;
        }

        return Origin;
    }


    public static bool IsAuxbrain(string host) {
        host = NormalizeHost(host);
        if (host.Length == 0) return false;
        foreach (string s in Suffixes) {
            if (host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            string marker = "-dot-" + s;
            if (host.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) {
                string service = host[..^marker.Length];
                if (service.Length > 0 && !service.Contains('.') &&
                    service[0] != '-' && service[^1] != '-' &&
                    service.All(c => char.IsLetterOrDigit(c) || c == '-')) {
                    return true;
                }
            }
        }

        return false;
    }


    public static string NormalizeHost(string authority) {
        if (string.IsNullOrEmpty(authority) || authority.Contains('/')) return "";


        if (authority[0] == '[') {
            int close = authority.IndexOf(']');
            return close > 1 ? authority[1..close] : "";
        }

        int colon = authority.IndexOf(':');
        return colon < 0 ? authority : authority.IndexOf(':', colon + 1) >= 0 ? "" : authority[..colon];
    }
}
