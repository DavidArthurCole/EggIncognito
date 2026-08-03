namespace EggIncognito.Services.ProtoExtract;

public enum MessageDiffKind { Added, Removed, Renamed, Modified }

public enum FieldChangeKind { Added, Removed, Changed }

public sealed record FieldChange(FieldChangeKind Kind, int Number, ProtoField? Old, ProtoField? New);

public sealed record EnumValueChange(FieldChangeKind Kind, string EnumName, int Number, ProtoEnumValue? Old, ProtoEnumValue? New);

public sealed record MessageDiff(
    MessageDiffKind Kind,
    string? OldPath,
    string? NewPath,
    IReadOnlyList<FieldChange> FieldChanges,
    IReadOnlyList<EnumValueChange> EnumChanges,
    IReadOnlyList<string> Body) {
    public string DisplayPath => Kind switch {
        MessageDiffKind.Removed => OldPath ?? "",
        _ => NewPath ?? ""
    };
}

public sealed record ProtoDiffResult(IReadOnlyList<MessageDiff> Entries) {
    public bool IsEmpty => Entries.Count == 0;
    public int AddedMessages => Entries.Count(e => e.Kind == MessageDiffKind.Added);
    public int RemovedMessages => Entries.Count(e => e.Kind == MessageDiffKind.Removed);
    public int RenamedMessages => Entries.Count(e => e.Kind == MessageDiffKind.Renamed);
    public int ModifiedMessages => Entries.Count(e => e.Kind == MessageDiffKind.Modified);
    public int FieldChangeCount => Entries.Sum(e => e.FieldChanges.Count + e.EnumChanges.Count);
}
