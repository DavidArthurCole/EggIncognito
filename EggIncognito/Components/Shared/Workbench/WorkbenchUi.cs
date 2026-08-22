using EggIncognito.Models.Shared;

namespace EggIncognito.Components.Shared.Workbench;

public enum WorkbenchSize {
    Regular,
    Wide
}

public enum WorkbenchTone {
    Normal,
    Muted,
    Warn,
    Bad
}

public sealed record WorkbenchNotice(string Text, StatusNoteKind Severity = StatusNoteKind.Info);
