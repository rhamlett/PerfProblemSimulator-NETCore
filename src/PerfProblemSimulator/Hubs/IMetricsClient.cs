using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Hubs;

/// <summary>
/// Interface for SignalR metrics client methods.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This interface defines the methods that can be called on connected clients.
/// SignalR uses this for strongly-typed hub methods, providing compile-time
/// safety instead of magic strings.
/// </para>
/// <para>
/// When the server calls <c>Clients.All.ReceiveMetrics(snapshot)</c>, SignalR
/// automatically serializes the snapshot and sends it to all connected browsers
/// via WebSockets (or fallback transports).
/// </para>
/// </remarks>
public interface IMetricsClient
{
    /// <summary>
    /// Receives a metrics snapshot update.
    /// </summary>
    /// <param name="snapshot">The latest metrics snapshot.</param>
    Task ReceiveMetrics(MetricsSnapshot snapshot);

    /// <summary>
    /// Receives a simulation started notification.
    /// </summary>
    /// <param name="simulationType">Type of simulation that started.</param>
    /// <param name="simulationId">ID of the simulation.</param>
    Task SimulationStarted(string simulationType, Guid simulationId);

    /// <summary>
    /// Receives a simulation completed notification.
    /// </summary>
    /// <param name="simulationType">Type of simulation that completed.</param>
    /// <param name="simulationId">ID of the simulation.</param>
    Task SimulationCompleted(string simulationType, Guid simulationId);
}
