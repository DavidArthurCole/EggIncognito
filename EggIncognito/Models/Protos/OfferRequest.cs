namespace EggIncognito.Models.Protos;

public sealed record OfferRequest(
    string Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion,
    string? Package,
    string ProtoSha,
    string ProtoText,
    string? MessageIndex);
