namespace EggIncognito.Models.Docs;

public sealed record SetSubjectTags(string SubjectKind, string SubjectKey, long[] TagIds);
