namespace EggIncognito.Models.Registry;

public sealed record ProtoMetaRow(string Key, string A, string B) {
    public bool Same => string.Equals(A, B, StringComparison.OrdinalIgnoreCase);
}
