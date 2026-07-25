namespace EggIncognito.Services;

public sealed record ApiError(string Error, string? Resolution, int Status, object? Details = null);
