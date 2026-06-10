using System.Text.Json.Serialization;

namespace EggIncognito.Models;

// Mirrors synckit/contract.NewVersionEvent and the device farm's emitter. Field names are frozen to
// the wire contract. This is plain transport, so System.Text.Json is allowed here, unlike endpoint
// JSON which uses JsonParser.Default.
public sealed class NewVersionEvent
{
    [JsonPropertyName("package")]
    public string Package { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("apkRef")]
    public string ApkRef { get; set; } = "";

    [JsonPropertyName("protoSha")]
    public string ProtoSha { get; set; } = "";

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = "";
}
