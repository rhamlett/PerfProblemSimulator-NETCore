using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

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
    /// Sets the current simulation context and tracks the simulation start in Application Insights.
    /// </summary>
    /// <param name="simulationId">The simulation ID.</param>
    /// <param name="simulationType">The type of simulation.</param>
    /// <returns>A disposable that clears the context and tracks simulation end when disposed.</returns>
    IDisposable SetContext(Guid simulationId, string simulationType);
}

/// <summary>
/// Tracks the current simulation context using AsyncLocal for async flow.
/// Also tracks simulation events in Application Insights with the simulation ID.
/// </summary>
public class SimulationContext : ISimulationContext
{
    private static readonly AsyncLocal<Guid?> _currentSimulationId = new();
    private static readonly AsyncLocal<string?> _currentSimulationType = new();
    private readonly TelemetryClient? _telemetryClient;
    private readonly ILogger<SimulationContext> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationContext"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="telemetryClient">The Application Insights telemetry client (optional - null when App Insights not configured).</param>
    public SimulationContext(ILogger<SimulationContext> logger, TelemetryClient? telemetryClient = null)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
        
        if (_telemetryClient == null)
        {
            _logger.LogWarning("SimulationContext: TelemetryClient is null - Application Insights events will not be tracked. " +
                "Ensure APPLICATIONINSIGHTS_CONNECTION_STRING is set.");
        }
        else
        {
            _logger.LogInformation("SimulationContext: TelemetryClient initialized successfully");
        }
    }

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

        // Track simulation start event in Application Insights
        TrackSimulationEvent("SimulationStarted", simulationId, simulationType);

        // Also set tags on the current Activity for W3C trace context correlation
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("SimulationId", simulationId.ToString());
            activity.SetTag("SimulationType", simulationType);
        }

        return new ContextScope(this, simulationId, simulationType, previousId, previousType);
    }

    /// <summary>
    /// Tracks a simulation event in Application Insights.
    /// </summary>
    internal void TrackSimulationEvent(string eventName, Guid simulationId, string simulationType)
    {
        _logger.LogInformation(
            "Tracking App Insights event: {EventName} for simulation {SimulationId} ({SimulationType})",
            eventName, simulationId, simulationType);

        if (_telemetryClient == null)
        {
            _logger.LogDebug("TelemetryClient is null, skipping event tracking");
            return;
        }

        try
        {
            var properties = new Dictionary<string, string>
            {
                ["SimulationId"] = simulationId.ToString(),
                ["SimulationType"] = simulationType
            };

            _telemetryClient.TrackEvent(eventName, properties);
            
            // Flush to ensure event is sent immediately (important for short-lived simulations)
            _telemetryClient.Flush();
            
            _logger.LogDebug("Successfully tracked and flushed event {EventName}", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track App Insights event {EventName}", eventName);
        }
    }

    private class ContextScope : IDisposable
    {
        private readonly SimulationContext _context;
        private readonly Guid _simulationId;
        private readonly string _simulationType;
        private readonly Guid? _previousId;
        private readonly string? _previousType;
        private bool _disposed;

        public ContextScope(SimulationContext context, Guid simulationId, string simulationType, Guid? previousId, string? previousType)
        {
            _context = context;
            _simulationId = simulationId;
            _simulationType = simulationType;
            _previousId = previousId;
            _previousType = previousType;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Track simulation end event
            _context.TrackSimulationEvent("SimulationEnded", _simulationId, _simulationType);

            _currentSimulationId.Value = _previousId;
            _currentSimulationType.Value = _previousType;
        }
    }
}
