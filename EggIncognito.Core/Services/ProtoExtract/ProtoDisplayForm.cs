namespace EggIncognito.Core.Services.ProtoExtract;

public static class ProtoDisplayForm {
    public const string Canonical = "canonical";
    public const string Raw = "raw";

    public static (string A, string B, string Form) Pair(string? canonA, string rawA, string? canonB, string rawB) {
        if (!string.IsNullOrEmpty(canonA) && !string.IsNullOrEmpty(canonB)) return (canonA, canonB, Canonical);
        return (rawA, rawB, Raw);
    }
}
