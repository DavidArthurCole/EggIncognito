using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EggIncognito.Services;

// TEMP diagnostic for silent StartCircuit failures - revert once real error is captured.
// Runs earlier than any IHubFilter: covers circuit-factory activation itself, not just hub methods.
public sealed class CircuitDiagnosticsHandler(ILogger<CircuitDiagnosticsHandler> logger) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        logger.LogWarning("Circuit {CircuitId} opened", circuit.Id);
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        logger.LogWarning("Circuit {CircuitId} connection up", circuit.Id);
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
