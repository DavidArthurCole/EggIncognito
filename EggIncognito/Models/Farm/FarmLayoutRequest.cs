using System.Text.Json;

namespace EggIncognito.Models.Farm;

public sealed record FarmLayoutRequest {
    public string? Platform { get; init; }
    public string? ShowcaseId { get; init; }
    public JsonElement? FarmConfig { get; init; }
    public FarmStateDto? State { get; init; }
    public int? ChickenCount { get; init; }
}
