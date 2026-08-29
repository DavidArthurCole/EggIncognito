using EggIdentity.UI;

namespace EggIncognito.Services.Events;

public sealed class EventsWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "events";

    public override string? Hash() => HashPrefix;

    public override bool ApplyHash(string? hash) => OwnsHash(hash);
}
