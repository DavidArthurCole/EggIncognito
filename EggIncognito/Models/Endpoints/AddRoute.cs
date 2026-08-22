namespace EggIncognito.Models.Endpoints;

public sealed record AddRoute(
    string Path,
    string? RequestType,
    string? ResponseType,
    bool? RequestWrapped,
    bool? ResponseWrapped,
    string? RawResponse,
    bool? PathParam,
    bool? PathParamOnly);
