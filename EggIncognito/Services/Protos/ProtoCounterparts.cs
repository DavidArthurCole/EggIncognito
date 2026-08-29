using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Protos;

public sealed record ProtoCounterpart(string Platform, ProtoRegistryRow? Row, VersionLinkKind Kind) {
    public bool Found => Row is not null;

    public bool Weak => Kind == VersionLinkKind.ClientVersion;

    public string Reason => ProtoVersionTranslator.Describe(Kind);
}

public static class ProtoCounterparts {
    public static IReadOnlyList<string> Targets(string? platform) {
        if (string.IsNullOrWhiteSpace(platform)) return [];
        if (!ProtoRefParser.Known.Any(p => Same(p, platform))) return [];
        return [.. ProtoRefParser.Known.Where(p => !Same(p, platform))];
    }

    public static IReadOnlyList<ProtoCounterpart> For(
        ProtoRegistryRow? source, IReadOnlyList<ProtoRegistryRow> registry) {
        if (source is null) return [];

        var links = new List<ProtoCounterpart>();
        foreach (string platform in Targets(source.Platform)) {
            VersionLink<ProtoRegistryRow> link =
                ProtoVersionTranslator.Translate(source, platform, registry, row => row.Key());
            links.Add(new ProtoCounterpart(platform, link.Row, link.Kind));
        }

        return links;
    }

    private static bool Same(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
