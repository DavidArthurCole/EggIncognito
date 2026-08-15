namespace EggIncognito.Services.Workbench;

public enum WorkbenchStatusKind {
    Queued,
    Running,
    Done,
    Error,
    Info,
    Muted
}

public static class WorkbenchStatus {
    private static readonly Dictionary<string, WorkbenchStatusKind> Known = new(StringComparer.OrdinalIgnoreCase) {
        ["queued"] = WorkbenchStatusKind.Queued,
        ["pending"] = WorkbenchStatusKind.Queued,
        ["running"] = WorkbenchStatusKind.Running,
        ["run"] = WorkbenchStatusKind.Running,
        ["succeeded"] = WorkbenchStatusKind.Done,
        ["done"] = WorkbenchStatusKind.Done,
        ["ok"] = WorkbenchStatusKind.Done,
        ["failed"] = WorkbenchStatusKind.Error,
        ["error"] = WorkbenchStatusKind.Error,
        ["err"] = WorkbenchStatusKind.Error,
        ["info"] = WorkbenchStatusKind.Info,
        ["offer"] = WorkbenchStatusKind.Info,
        ["offerable"] = WorkbenchStatusKind.Info
    };

    public static string Class(WorkbenchStatusKind kind) {
        return kind switch {
            WorkbenchStatusKind.Queued => "wb-st-queued",
            WorkbenchStatusKind.Running => "wb-st-run",
            WorkbenchStatusKind.Done => "wb-st-done",
            WorkbenchStatusKind.Error => "wb-st-err",
            WorkbenchStatusKind.Info => "wb-st-offer",
            _ => "wb-st-muted"
        };
    }

    public static WorkbenchStatusKind Parse(string? value) {
        if (value is not { Length: > 0 }) return WorkbenchStatusKind.Muted;
        return Known.TryGetValue(value, out var kind) ? kind : WorkbenchStatusKind.Muted;
    }
}
