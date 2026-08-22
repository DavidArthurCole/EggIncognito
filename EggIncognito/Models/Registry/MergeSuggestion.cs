namespace EggIncognito.Models.Registry;

public sealed record MergeSuggestion(string AppVersion, string ProtoSha, List<MergeMember> Members);
