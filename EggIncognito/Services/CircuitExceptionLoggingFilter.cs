using Microsoft.AspNetCore.SignalR;

namespace EggIncognito.Services;

// TEMP diagnostic for silent StartCircuit failures - revert once real error is captured.
// HubOptions.EnableDetailedErrors only covers hub-method invocations, not OnConnectedAsync
// (where StartCircuit runs), so this filter logs the raw exception before SignalR swallows it.
public sealed class CircuitExceptionLoggingFilter(ILogger<CircuitExceptionLoggingFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {Method} threw", invocationContext.HubMethodName);
            throw;
        }
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub OnConnectedAsync threw for connection {ConnectionId}", context.Context.ConnectionId);
            throw;
        }
    }
}
