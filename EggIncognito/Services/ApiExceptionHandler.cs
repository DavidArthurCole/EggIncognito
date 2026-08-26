using Microsoft.AspNetCore.Diagnostics;

namespace EggIncognito.Services;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken) {
        ApiError error;
        if (exception is ApiException api) {
            logger.LogWarning("{Path} -> {Status} {Error}",
                httpContext.Request.Path, api.Status, api.Error);
            error = api.ToApiError();
        } else {
            if (httpContext.Request.Headers.Accept.ToString()
                    .Contains("text/html", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            logger.LogError(exception, "Unhandled exception at {Path}", httpContext.Request.Path);
            error = new ApiError(
                "internal server error",
                "Check the server logs (Logs tab or logs/ file) for the stack trace.",
                StatusCodes.Status500InternalServerError);
        }

        httpContext.Response.StatusCode = error.Status;
        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }
}
