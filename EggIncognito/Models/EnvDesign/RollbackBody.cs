using System.Text.Json.Serialization;

namespace EggIncognito.Models.EnvDesign;

public sealed record RollbackBody([property: JsonRequired] int VersionNo);
