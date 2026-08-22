using EggIncognito.Services.Protos;

namespace EggIncognito.Models.Registry;

public sealed record Release(long Key, IReadOnlyList<ProtoRegistryRow> Members) {
    public ProtoRegistryRow Primary => Members[0];
    public bool IsGrouped => Members.Count > 1;

    public IEnumerable<ProtoRegistryRow> Platforms =>
        Members.DistinctBy(m => m.Platform, StringComparer.OrdinalIgnoreCase);
}
