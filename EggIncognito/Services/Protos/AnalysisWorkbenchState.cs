namespace EggIncognito.Services.Protos;

public sealed record ExtractResult {
    public bool Ok { get; init; }
    public string? Proto { get; init; }
    public string? Diagnostics { get; init; }
    public string? ProtoSha { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
    public string? AppVersion { get; init; }
    public string? Build { get; init; }
    public int? ClientVersion { get; init; }
    public string? FileName { get; init; }
    public string? FileSha { get; init; }
    public long? FileSize { get; init; }
    public long? StrippedSize { get; init; }
    public long? UploadedSize { get; init; }
}

public sealed class StagedEntry {
    public Guid Id { get; } = Guid.NewGuid();
    public required int Token { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
    public string Status { get; set; } = "queued";
    public string? Step { get; set; }
    public ExtractResult? Result { get; set; }
    public string? Error { get; set; }
    public string Platform { get; set; } = "ios";
    public string AppVersion { get; set; } = "";
    public string Build { get; set; } = "";
    public string ClientVersionText { get; set; } = "";
    public bool IsDone => Status == "done";
    public bool IsAnalyzed => IsDone && Result is { Ok: true };
}

public sealed record GroupStatus(bool Known, bool Pending, bool Offered, bool Failed = false) {
    public bool Offerable => !Known && !Pending && !Offered && !Failed;
}

public sealed class AnalysisWorkbenchState {
    public List<StagedEntry> Entries { get; } = [];
    public Guid? SelectedId { get; set; }
    public Dictionary<string, Guid> GroupWinners { get; } = [];
    public Dictionary<string, GroupStatus> GroupStatuses { get; } = [];

    public StagedEntry? Find(Guid? id) {
        return id is { } g ? Entries.FirstOrDefault(e => e.Id == g) : null;
    }
}
