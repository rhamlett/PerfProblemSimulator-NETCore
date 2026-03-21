using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
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
    void TrackSimulationStarted(Guid simulationId, string simulationType);

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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SimulationContext> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationContext"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving TelemetryClient.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SimulationContext(IServiceProvider serviceProvider, ILogger<SimulationContext> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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
    public void TrackSimulationStarted(Guid simulationId, string simulationType)
    {
        TrackSimulationEvent("SimulationStarted", simulationId, simulationType);
    }

    /// <inheritdoc />
    public void TrackSimulationEnded(Guid simulationId, string simulationType)
    {
        TrackSimulationEvent("SimulationEnded", simulationId, simulationType);
    }

    /// <summary>
    /// Tracks a simulation event in Application Insights.
    /// </summary>
    internal void TrackSimulationEvent(string eventName, Guid simulationId, string simulationType)
    {
        _logger.LogInformation(
            "Tracking App Insights event: {EventName} for simulation {SimulationId} ({SimulationType})",
            eventName, simulationId, simulationType);

        try
        {
            // Lazily resolve TelemetryClient - it may not be registered if App Insights isn't configured
            var telemetryClient = _serviceProvider.GetService<TelemetryClient>();
            
            if (telemetryClient == null)
            {
                _logger.LogDebug("TelemetryClient not available (App Insights not configured), skipping event tracking");
                return;
            }

            var properties = new Dictionary<string, string>
            {
                ["SimulationId"] = simulationId.ToString(),
                ["SimulationType"] = simulationType
            };

            telemetryClient.TrackEvent(eventName, properties);
            
            // Flush to ensure event is sent immediately (important for short-lived simulations)
            telemetryClient.Flush();
            
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
