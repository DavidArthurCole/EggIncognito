namespace EggIncognito.Core.Services.Devices;

public sealed record IntegrityModuleSpec(
    string Name, string? Repo, string? Url, string? Tag, string? Sha256, bool RebootAfter) {
    public bool Pinned => !string.IsNullOrWhiteSpace(Tag) && !string.IsNullOrWhiteSpace(Sha256)
                          || !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Sha256);
}
