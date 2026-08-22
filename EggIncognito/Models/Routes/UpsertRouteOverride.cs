namespace EggIncognito.Models.Routes;

public sealed record UpsertRouteOverride(
    string? Request,
    string? Response,
    bool? RequestWrapped,
    bool? ResponseWrapped,
    bool? PathParam);
