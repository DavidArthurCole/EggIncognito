using EggIncognito.Models.Shared;

namespace EggIncognito.Models.Theme;

public sealed record ThemeStatus(string Message, StatusNoteKind Kind, ThemeStatusSource Source, bool NeedsReload = false);
