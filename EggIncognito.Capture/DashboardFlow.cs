namespace EggIncognito.Capture;

public sealed record DashboardFlow(
    long Id,
    string Timestamp,
    string Path,
    string Method,
    int Status,
    string? RequestJson,
    string? ResponseJson,
    string ResponseB64,
    string? RequestDataB64,

    string? RequestType = null,
    string? ResponseType = null,
    bool Known = false,

    string Outcome = "",

    int DiffAdded = 0,
    int DiffRemoved = 0,

    string? RequestJsonRaw = null,
    string? ResponseJsonRaw = null,

    string Url = "",

    IReadOnlyList<DashboardHeader>? RequestHeaders = null,
    IReadOnlyList<DashboardHeader>? ResponseHeaders = null,
    IReadOnlyList<DashboardHeader>? RequestHeadersRaw = null,
    IReadOnlyList<DashboardHeader>? ResponseHeadersRaw = null,

    bool ResponseIsAck = false,

    string? ResponseText = null,


    bool Saved = false,

    EggIncognito.Services.RinfoHarvester.ObservedVersion? Observed = null);

public sealed record DashboardHeader(string Name, string Value, bool Sensitive = false);
