namespace EggIncognito.Models.Inspector;

public sealed record SendRequest(
    string? Url,
    string FormBody,
    string? ResponseType,
    bool Sealed = false,
    bool? ResponseWrapped = null,
    string? Path = null,
    string? PathParam = null);
