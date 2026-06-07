// EggIncognito/Services/ApiException.cs
//
// Throw this for any error response that should short-circuit a request. The
// ApiExceptionHandler turns it into an ApiError JSON body with the matching status.
// Every throw site must supply a Resolution - the "possible fix" the user sees.

namespace EggIncognito.Services;

public sealed class ApiException(string error, string? resolution, int status = 400, object? details = null)
    : Exception(error)
{
    public string Error { get; } = error;
    public string? Resolution { get; } = resolution;
    public int Status { get; } = status;
    public object? Details { get; } = details;

    public ApiError ToApiError() => new(Error, Resolution, Status, Details);
}
