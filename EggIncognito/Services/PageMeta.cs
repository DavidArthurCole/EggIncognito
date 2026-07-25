namespace EggIncognito.Services;

public static class PageMeta {
    public static readonly Meta Default = new(
        "EggIncognito - a toolkit for the Egg, Inc. API",
        "A toolkit for the Egg, Inc. API: a byte-identical mock server, a request inspector, a live " +
        "capture proxy, a versioned proto registry, and a physical device farm. Test tooling without real " +
        "accounts or rate limits.");


    private static readonly (string Prefix, Meta Meta)[] Routes = [
        ("/inspector", new Meta(
            "EggIncognito - Inspector",
            "Build, sign, send, and decode any Egg, Inc. API request by hand. Every transform in the " +
            "transport pipeline is visualized, from proto to signed wire format and back.")),
        ("/capture", new Meta(
            "EggIncognito - Capture",
            "A TLS-intercepting proxy that records live Egg, Inc. game traffic into reusable endpoint " +
            "fixtures.")),
        ("/protos", new Meta(
            "EggIncognito - Proto Registry",
            "A versioned registry of Egg, Inc. proto definitions. Drop an .ipa/.apk to extract its schema, " +
            "or browse detected builds per platform across versions.")),
        ("/docs", new Meta(
            "EggIncognito - Docs",
            "Per-message and per-endpoint documentation for the Egg, Inc. API, with tags and a full proto " +
            "reference.")),
        ("/import", new Meta(
            "EggIncognito - Import",
            "Turn a captured HAR of Egg, Inc. traffic into reusable mock endpoints.")),
        ("/admin", new Meta(
            "EggIncognito - Admin",
            "User roles and shared-store review for the EggIncognito registry."))
    ];

    public static Meta For(string? path) {
        if (string.IsNullOrEmpty(path)) return Default;
        var best = Default;
        int bestLen = -1;
        foreach ((string prefix, var meta) in Routes) {
            if ((path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                && prefix.Length > bestLen) {
                best = meta;
                bestLen = prefix.Length;
            }
        }

        return best;
    }

    public readonly record struct Meta(string Title, string Description);
}
