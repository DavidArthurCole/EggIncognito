namespace EggIncognito.Models.Inspector;

public record RouteOverrideDto(string? Request, string? Response, bool? RequestWrapped, bool? ResponseWrapped, bool? PathParam);
