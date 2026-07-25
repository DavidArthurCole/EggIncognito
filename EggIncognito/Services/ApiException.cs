namespace EggIncognito.Services;

public sealed class ApiException(string error, string? resolution, int status = 400, object? details = null)
    : Exception(error) {
    public string Error { get; } = error;
    public string? Resolution { get; } = resolution;
    public int Status { get; } = status;
    public object? Details { get; } = details;

    public ApiError ToApiError() => new(Error, Resolution, Status, Details);
}
