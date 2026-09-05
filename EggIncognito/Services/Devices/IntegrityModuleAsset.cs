using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed record IntegrityModuleAsset(IntegrityModuleSpec Spec, string ModuleId, string? Version, byte[] Zip);
