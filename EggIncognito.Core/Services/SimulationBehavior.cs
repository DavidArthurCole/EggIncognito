using System.Text.Json.Serialization;

namespace EggIncognito.Services;

public record SimulationBehavior(
    string Name,
    string Description,
    int HttpStatus,
    [property: JsonIgnore] Func<byte[]>? Body = null,
    IReadOnlyList<string>? Endpoints = null,
    [property: JsonIgnore] IReadOnlyDictionary<string, string>? ExtraHeaders = null
);
