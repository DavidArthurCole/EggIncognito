namespace EggIncognito.Models.Protos;

public sealed record ApproveRequest(string? Platform, string? AppVersion, string? Build, string? ClientVersion);
