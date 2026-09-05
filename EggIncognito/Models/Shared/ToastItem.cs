namespace EggIncognito.Models.Shared;

public sealed record ToastItem(
    Guid Id,
    StatusNoteKind Kind,
    string Text,
    DateTimeOffset At,
    bool Sticky,
    string? ActionLabel = null,
    Action? Action = null);
