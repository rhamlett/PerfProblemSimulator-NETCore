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
    /// <param name="trackStart">Whether to track the SimulationStarted event. Set to false if already tracked externally.</param>
    /// <returns>A disposable that clears the context and tracks simulation end when disposed.</returns>
    IDisposable SetContext(Guid simulationId, string simulationType, bool trackStart = true);

    /// <summary>
    /// Tracks a SimulationStarted event in Application Insights.
    /// Use this to track the event synchronously before starting CPU-intensive background work.
    /// </summary>
    /// <param name="simulationId">The simulation ID.</param>
    /// <param name="simulationType">The type of simulation.</param>
    /// <param name="waitForTransmission">If true, blocks until telemetry transmission completes. Use for CPU-intensive simulations.</param>
    void TrackSimulationStarted(Guid simulationId, string simulationType, bool waitForTransmission = false);

    /// <summary>
    /// Tracks a SimulationEnded event in Application Insights.
    /// </summary>
    /// <param name="simulationId">The simulation ID.</param>
    /// <param name="simulationType">The type of simulation.</param>
    void TrackSimulationEnded(Guid simulationId, string simulationType);
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
    /// <param name="telemetryClient">Application Insights TelemetryClient (optional - may be null if not configured).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SimulationContext(ILogger<SimulationContext> logger, TelemetryClient? telemetryClient = null)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
        
        _logger.LogWarning(
            "🔧 SimulationContext initialized. TelemetryClient available: {Available}",
            _telemetryClient != null);
    }

    /// <inheritdoc />
    public Guid? CurrentSimulationId => _currentSimulationId.Value;

    /// <inheritdoc />
    public string? CurrentSimulationType => _currentSimulationType.Value;

    /// <inheritdoc />
    public IDisposable SetContext(Guid simulationId, string simulationType, bool trackStart = true)
    {
        var previousId = _currentSimulationId.Value;
        var previousType = _currentSimulationType.Value;

        _currentSimulationId.Value = simulationId;
        _currentSimulationType.Value = simulationType;

        // Track simulation start event in Application Insights (unless already tracked externally)
        if (trackStart)
        {
            TrackSimulationEvent("SimulationStarted", simulationId, simulationType);
        }

        // Also set tags on the current Activity for W3C trace context correlation
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("SimulationId", simulationId.ToString());
            activity.SetTag("SimulationType", simulationType);
        }

        return new ContextScope(this, simulationId, simulationType, previousId, previousType);
    }

    /// <inheritdoc />
    public void TrackSimulationStarted(Guid simulationId, string simulationType, bool waitForTransmission = false)
    {
        TrackSimulationEvent("SimulationStarted", simulationId, simulationType, waitForTransmission);
    }

    /// <inheritdoc />
    public void TrackSimulationEnded(Guid simulationId, string simulationType)
    {
        TrackSimulationEvent("SimulationEnded", simulationId, simulationType);
    }

    /// <summary>
    /// Tracks a simulation event in Application Insights.
    /// </summary>
    /// <param name="eventName">The event name (SimulationStarted/SimulationEnded).</param>
    /// <param name="simulationId">The simulation ID.</param>
    /// <param name="simulationType">The simulation type.</param>
    /// <param name="waitForTransmission">If true, waits briefly to allow transmission (use for CPU-intensive simulations).</param>
    internal void TrackSimulationEvent(string eventName, Guid simulationId, string simulationType, bool waitForTransmission = false)
    {
        _logger.LogWarning(
            "📊 Tracking App Insights event: {EventName} for simulation {SimulationId} ({SimulationType})",
            eventName, simulationId, simulationType);

        try
        {
            if (_telemetryClient == null)
            {
                _logger.LogWarning("⚠️ TelemetryClient not available, skipping event tracking");
                return;
            }

            var properties = new Dictionary<string, string>
            {
                ["SimulationId"] = simulationId.ToString(),
                ["SimulationType"] = simulationType
            };

            _telemetryClient.TrackEvent(eventName, properties);
            _logger.LogWarning("📊 TrackEvent called for {EventName}", eventName);
            
            // Note: Don't call Flush() - SDK v3 has a bug where it throws NullReferenceException
            // The SDK will batch and send telemetry automatically
            
            // For CPU-intensive operations, wait briefly to allow the SDK to send
            // before background threads saturate all cores
            if (waitForTransmission)
            {
                _logger.LogWarning("📊 Waiting 1s for telemetry transmission...");
                Thread.Sleep(1000);
            }
            
            _logger.LogWarning("📊 Successfully tracked event {EventName}", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to track App Insights event {EventName}", eventName);
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
