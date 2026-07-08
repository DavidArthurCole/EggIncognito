using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Components.Inspector;

// Deserialization shapes for the Inspector controller responses. The build/send/decode calls go through
// the controllers, which own the salt build, egress, and host allowlist.

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

// POST /api/tools/diagnose. Mirrors WireForensics.DiagnoseResult by field name so JSON binds 1:1; kept
// separate because Core has no web dependency. Error is the in-band base64-decode failure field.
public sealed record DiagnoseDto(
    bool Ok,
    int TotalLen,
    int NodesWalked,
    DiagnoseErrorDto? FirstError,
    HexWindowDto? HexAround,
    List<SalvagedDto>? Salvaged,
    List<WireNodeDto>? Tree,
    RecoveryDto? Recovered,
    string? Error);

public sealed record DiagnoseErrorDto(int Offset, string Path, string? ResolvedPath, string Message);
public sealed record HexWindowDto(int From, int To, int ErrorIndexInWindow, string Hex);
public sealed record SalvagedDto(int Offset, string Text);
public sealed record WireNodeDto(
    string Path, string? ResolvedName, int Field, string Wire, int Offset,
    int? Len, bool SchemaMismatch, List<WireNodeDto>? Children);
public sealed record RecoveryDto(int AlignedAt, int SkippedBytes, List<RecoveredFieldDto>? Fields);
public sealed record RecoveredFieldDto(int Field, string? ResolvedName, string Wire, string Value, bool Bad);
