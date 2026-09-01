namespace EggIncognito.Services.Admin;

public sealed class AdminWorkbenchState {
    public string SelectedPane { get; private set; } = AdminPanes.Traffic;
    public HashSet<string> Visited { get; } = [AdminPanes.Traffic];
    public HashSet<string> Expanded { get; } = [];
    public Dictionary<string, string> Filters { get; } = [with(StringComparer.Ordinal)];

    public void Select(string key) {
        SelectedPane = key;
        Visited.Add(key);
    }
}
