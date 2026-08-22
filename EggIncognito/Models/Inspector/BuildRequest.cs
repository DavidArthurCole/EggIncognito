using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggIncognito.Models.Inspector;

public sealed record BuildRequest(
    string Path,
    string RequestType,
    [property: JsonRequired] bool Wrap,
    JsonElement? Fields,
    JsonElement? Env,
    string? Salt);
