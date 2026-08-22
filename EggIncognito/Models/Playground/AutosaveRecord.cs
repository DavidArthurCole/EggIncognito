namespace EggIncognito.Models.Playground;

public sealed class AutosaveRecord {
    public string Json { get; } = "";
    public string Version { get; set; } = "";
    public long SavedAt { get; set; }
}
