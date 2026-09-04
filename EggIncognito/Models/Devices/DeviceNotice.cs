using EggIncognito.Models.Shared;

namespace EggIncognito.Models.Devices;

public sealed record DeviceNotice(string Text, StatusNoteKind Kind);
