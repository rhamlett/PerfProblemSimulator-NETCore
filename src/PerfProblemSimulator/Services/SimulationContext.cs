using System.Diagnostics;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for tracking the current simulation context.
/// Uses AsyncLocal to flow the simulation ID across async calls.
/// </summary>
public interface ISimulationContext
{
    /// <summary>
    /// Gets the current simulation ID, if any.
    /// </summary>
    Guid? CurrentSimulationId { get; }

    /// <summary>
    /// Gets the current simulation type, if any.
    /// </summary>
    string? CurrentSimulationType { get; }

    /// <summary>
    /// Sets the current simulation context.
    /// </summary>
    /// <param name="simulationId">The simulation ID.</param>
    /// <param name="simulationType">The type of simulation.</param>
    /// <returns>A disposable that clears the context when disposed.</returns>
    IDisposable SetContext(Guid simulationId, string simulationType);
}

/// <summary>
/// Tracks the current simulation context using AsyncLocal for async flow.
/// Also sets Activity tags for distributed tracing correlation.
/// </summary>
public class SimulationContext : ISimulationContext
{
    private static readonly AsyncLocal<Guid?> _currentSimulationId = new();
    private static readonly AsyncLocal<string?> _currentSimulationType = new();

    /// <inheritdoc />
    public Guid? CurrentSimulationId => _currentSimulationId.Value;

    /// <inheritdoc />
    public string? CurrentSimulationType => _currentSimulationType.Value;

    /// <inheritdoc />
    public IDisposable SetContext(Guid simulationId, string simulationType)
    {
        var previousId = _currentSimulationId.Value;
        var previousType = _currentSimulationType.Value;

        _currentSimulationId.Value = simulationId;
        _currentSimulationType.Value = simulationType;

        // Also set tags on the current Activity for W3C trace context correlation
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("simulation.id", simulationId.ToString());
            activity.SetTag("simulation.type", simulationType);
        }

        return new ContextScope(previousId, previousType);
    }

    private class ContextScope : IDisposable
    {
        private readonly Guid? _previousId;
        private readonly string? _previousType;
        private bool _disposed;

        public ContextScope(Guid? previousId, string? previousType)
        {
            _previousId = previousId;
            _previousType = previousType;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _currentSimulationId.Value = _previousId;
            _currentSimulationType.Value = _previousType;
        }
    }
}
