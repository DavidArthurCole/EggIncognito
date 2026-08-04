namespace EggIncognito.Services;

public static class AuxbrainHosts {
    public const string Origin = "https://www.auxbrain.com";

    private static readonly string[] Suffixes = [
        "auxbrain.com",
        "auxbrainhome.appspot.com"
    ];


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
