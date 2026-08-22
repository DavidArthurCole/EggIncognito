using System.Text.Json.Nodes;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Models.Playground;

public sealed class FarmDesignPayload {
    public int Schema { get; set; } = 2;
    public string Platform { get; set; } = Platforms.Ios;
    public FarmStateModel? State { get; set; }
    public JsonNode? FarmConfig { get; set; }
    public int ChickenCount { get; set; }
    public string? ShowcaseId { get; set; }
    public string Background { get; set; } = "#1a1a1f";
    public bool BackgroundTransparent { get; set; } = true;
}
