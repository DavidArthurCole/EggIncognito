using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Components.Inspector;

public sealed record BuildResponse(
    List<TransportStage>? Stages,
    string? FinalBase64,
    string? FinalFormBody,
    bool CanSign,
    string? Error,
    string? Resolution,
    JsonElement? Details);

public sealed record SendResponse(
    int? Status,
    string? RawBase64,
    List<TransportStage>? Stages,
    string? Json,
    string? Error,
    string? Resolution,
    bool WrappedMismatch = false);

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
    string Path,
    string? ResolvedName,
    int Field,
    string Wire,
    int Offset,
    int? Len,
    bool SchemaMismatch,
    List<WireNodeDto>? Children);

public sealed record RecoveryDto(int AlignedAt, int SkippedBytes, List<RecoveredFieldDto>? Fields);

public sealed record RecoveredFieldDto(int Field, string? ResolvedName, string Wire, string Value, bool Bad);
