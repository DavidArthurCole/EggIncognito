// EggIncognito/Services/ApiExceptionHandler.cs
//
// App-wide exception handler (registered via AddExceptionHandler + UseExceptionHandler).
// Maps ApiException to its structured ApiError body; maps anything unhandled to a generic
// 500 whose resolution points at the logs. Applies to all controllers and minimal-API
// endpoints.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace EggIncognito.Services;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ApiError error;
        if (exception is ApiException api)
        {
            // Expected, handled failure - log at Warning, no stack noise.
            logger.LogWarning("{Path} -> {Status} {Error}",
                httpContext.Request.Path, api.Status, api.Error);
            error = api.ToApiError();
        }
        else
        {
            logger.LogError(exception, "Unhandled exception at {Path}", httpContext.Request.Path);
            error = new ApiError(
                Error: "internal server error",
                Resolution: "Check the server logs (Logs tab or logs/ file) for the stack trace.",
                Status: StatusCodes.Status500InternalServerError);
        }

        httpContext.Response.StatusCode = error.Status;
        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }
}
