namespace EggIncognito.Services.ProtoExtract;

public sealed record ProtoDiffSummary(
    int MessagesAdded,
    int MessagesRemoved,
    int MessagesRenamed,
    int MessagesModified,
    int FieldsAdded,
    int FieldsRemoved,
    int FieldsChanged,
    int EnumValuesChanged,
    int LinesAdded,
    int LinesRemoved) {
    public bool IsEmpty =>
        MessagesAdded == 0 && MessagesRemoved == 0 && MessagesRenamed == 0 && MessagesModified == 0
        && FieldsAdded == 0 && FieldsRemoved == 0 && FieldsChanged == 0 && EnumValuesChanged == 0
        && LinesAdded == 0 && LinesRemoved == 0;

    public static ProtoDiffSummary From(ProtoDiffResult structural, IReadOnlyList<DiffOp> lineOps) {
        int fieldsAdded = 0;
        int fieldsRemoved = 0;
        int fieldsChanged = 0;
        int enumValuesChanged = 0;
        foreach (var entry in structural.Entries) {
            foreach (var change in entry.FieldChanges) {
                if (change.Kind == FieldChangeKind.Added) fieldsAdded++;
                else if (change.Kind == FieldChangeKind.Removed) fieldsRemoved++;
                else fieldsChanged++;
            }

            enumValuesChanged += entry.EnumChanges.Count;
        }

        int linesAdded = 0;
        int linesRemoved = 0;
        foreach (var op in lineOps) {
            if (op.Kind == DiffOpKind.Insert) linesAdded += op.BLength;
            else if (op.Kind == DiffOpKind.Delete) linesRemoved += op.ALength;
        }

        return new ProtoDiffSummary(
            structural.AddedMessages,
            structural.RemovedMessages,
            structural.RenamedMessages,
            structural.ModifiedMessages,
            fieldsAdded,
            fieldsRemoved,
            fieldsChanged,
            enumValuesChanged,
            linesAdded,
            linesRemoved);
    }
}
