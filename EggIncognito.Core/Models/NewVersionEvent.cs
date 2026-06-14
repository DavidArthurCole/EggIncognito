using System.Text.Json.Serialization;

namespace EggIncognito.Core.Models;

// Shared frozen contract. Mirrors synckit/contract.NewVersionEvent and the device farm's emitter;
// the superset source of truth shared by the runner (producer) and EventsController (consumer).
// Field names are frozen to the wire contract. This is plain transport, so System.Text.Json is
// allowed here, unlike endpoint JSON which uses JsonParser.Default.
public sealed class NewVersionEvent
{
    [JsonPropertyName("package")]
    public string Package { get; set; } = "";

    // Legacy single version, kept for back-compat with old farm emitters. Treated as the appVersion
    // fallback: when AppVersion is absent the ingest uses this. New emitters send the three below.
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    // appVersion: user-facing label (e.g. 1.35.7), not unique across builds.
    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    // build: monotonic versionCode (e.g. 111343), unique per build, the registry row key.
    [JsonPropertyName("build")]
    public string Build { get; set; } = "";

    // clientVersion: proto/API client version (e.g. 72), best-effort, nullable until extracted.
    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("apkRef")]
    public string ApkRef { get; set; } = "";

    [JsonPropertyName("protoSha")]
    public string ProtoSha { get; set; } = "";

    [JsonPropertyName("detectedAt")]
    public string DetectedAt { get; set; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "android";

    [JsonPropertyName("protoTextB64")]
    public string? ProtoTextB64 { get; set; }
}
