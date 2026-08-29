namespace EggIncognito.Models.Devices;

public sealed record UiSelectorHint(string By, string Label, string Value, int Index, int Matches, string Snippet) {
    public bool Unique => Matches == 1;
}
