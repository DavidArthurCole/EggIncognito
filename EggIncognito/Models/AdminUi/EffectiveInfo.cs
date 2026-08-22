namespace EggIncognito.Models.AdminUi;

public record EffectiveInfo(string? Request, string? Response, bool RequestWrapped, bool ResponseWrapped, bool PathParam);
