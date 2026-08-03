using EggIncognito.Services.ProtoExtract;

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
    public bool Known { get; set; }
    public bool Pending { get; set; }
    public bool Offerable { get; set; }
    public bool Offered { get; set; }
    public ProtoDiffResult? DiffVsLatest { get; set; }
    public string? DiffVsLatestLabel { get; set; }
    public bool IsDone => Status == "done";
}

public sealed class AnalysisWorkbenchState {
    public List<StagedEntry> Entries { get; } = [];
    public Guid? SelectedId { get; set; }
    public Guid? CompareA { get; set; }
    public Guid? CompareB { get; set; }
    public string View { get; set; } = "detail";

    public StagedEntry? Find(Guid? id) {
        return id is { } g ? Entries.FirstOrDefault(e => e.Id == g) : null;
    }
}
