namespace EggIncognito.Capture;

public sealed record ContributionDraft(
    string Kind,
    string Summary,
    string PayloadJson,
    string DedupeHash,
    string? ClientVersion);

public interface ICaptureContributionKind {
    string Kind { get; }
    IReadOnlyCollection<string> Routes { get; }
    ContributionDraft? Build(DashboardFlow flow);
}

public interface ICaptureContributionKinds {
    IReadOnlySet<string> AllRoutes { get; }
    IReadOnlyList<string> KindNames { get; }
    ICaptureContributionKind? For(string path);
}

public sealed class CaptureContributionKinds : ICaptureContributionKinds {
    private readonly Dictionary<string, ICaptureContributionKind> _byRoute;

    public CaptureContributionKinds(IEnumerable<ICaptureContributionKind> kinds) {
        _byRoute = [with(StringComparer.Ordinal)];
        var names = new List<string>();
        foreach (var kind in kinds) {
            names.Add(kind.Kind);
            foreach (string route in kind.Routes) _byRoute[route] = kind;
        }

        AllRoutes = _byRoute.Keys.ToHashSet(StringComparer.Ordinal);
        KindNames = names;
    }

    public IReadOnlySet<string> AllRoutes { get; }
    public IReadOnlyList<string> KindNames { get; }

    public ICaptureContributionKind? For(string path) => _byRoute.GetValueOrDefault(path);
}
