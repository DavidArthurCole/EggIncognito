using EggIncognito.Services.Protos;

namespace EggIncognito.Models.Registry;

public sealed class OfferGroup {
    public string Key { get; init; } = "";
    public string Platform { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public string Build { get; init; } = "";
    public string ClientVersionText { get; init; } = "";
    public List<StagedEntry> Members { get; } = [];
    public bool MultiSha { get; set; }
    public StagedEntry? Winner { get; set; }
    public GroupStatus? Status { get; set; }
    public bool Conflict => MultiSha && Winner is null;
    public string? Sha => Winner?.Result?.ProtoSha;
    public string? StatusKey => Sha is { Length: > 0 } s ? Key + "|" + s : null;
}
