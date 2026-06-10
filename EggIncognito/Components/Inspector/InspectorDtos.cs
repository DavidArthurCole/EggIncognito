using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Components.Inspector;

// Deserialization shapes for the Inspector controller responses. The build/send/decode calls go through
// the controllers (they own the salt build, egress, and host allowlist); these record the JSON they
// return so the components can render the pipeline + response without re-deriving the transport.

// POST /api/inspector/build
public sealed record BuildResponse(
    List<TransportStage>? Stages,
    string? FinalBase64,
    string? FinalFormBody,
    bool CanSign,
    // In-band ApiError fields when the build failed.
    string? Error,
    string? Resolution,
    JsonElement? Details);

// POST /api/inspector/send and /decode-response (status only on /send).
public sealed record SendResponse(
    int? Status,
    string? RawBase64,
    List<TransportStage>? Stages,
    string? Json,
    string? Error,
    string? Resolution);
