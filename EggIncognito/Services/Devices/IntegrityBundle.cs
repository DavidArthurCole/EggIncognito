using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed record IntegrityBundle(
    bool Ok,
    string? Error,
    PifProfile? Profile,
    string? PifPropText,
    string? KeyboxXml,
    string? KeyboxSource,
    IReadOnlyList<string> KeyboxSerials,
    string? KeyboxNote,
    string? PatchDate,
    IReadOnlyList<IntegrityModuleAsset> Modules,
    IReadOnlyList<string> Warnings) {
    public static IntegrityBundle Fail(string error) =>
        new(false, error, null, null, null, null, [], null, null, [], []);
}
