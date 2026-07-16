


namespace EggIncognito.Services;

public static class AuxbrainHosts
{
   
    private static readonly string[] Suffixes =
    [
        "auxbrain.com",
        "auxbrainhome.appspot.com",
    ];

   
   
   
   
    public static bool IsAuxbrain(string host)
    {
        host = NormalizeHost(host);
        if (host.Length == 0) return false;
        foreach (var s in Suffixes)
        {
           
            if (host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
                return true;

           
           
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

   
   
   
    public static string NormalizeHost(string authority)
    {
        if (string.IsNullOrEmpty(authority) || authority.Contains('/')) return "";

       
        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']');
            return close > 1 ? authority[1..close] : "";
        }

        var colon = authority.IndexOf(':');
        if (colon < 0) return authority;
       
       
        if (authority.IndexOf(':', colon + 1) >= 0) return "";
        return authority[..colon];
    }
}
