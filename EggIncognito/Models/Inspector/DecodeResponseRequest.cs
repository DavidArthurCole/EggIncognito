namespace EggIncognito.Models.Inspector;

public sealed record DecodeResponseRequest(string RawBase64, string? ResponseType, bool? ResponseWrapped = null);
