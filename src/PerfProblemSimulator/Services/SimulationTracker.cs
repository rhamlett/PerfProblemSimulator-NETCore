using PerfProblemSimulator.Models;
using System.Collections.Concurrent;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for tracking active simulations.
/// </summary>
public interface ISimulationTracker
{
    /// <summary>
    /// Event fired when a simulation is registered.
    /// </summary>
    event EventHandler<SimulationEventArgs>? SimulationStarted;

    /// <summary>
    /// Event fired when a simulation is unregistered (completed or cancelled).
    /// </summary>
    event EventHandler<SimulationEventArgs>? SimulationCompleted;

    /// <summary>
    /// Registers a new active simulation.
    /// </summary>
    /// <param name="simulationId">Unique identifier for the simulation.</param>
    /// <param name="type">Type of simulation being tracked.</param>
    /// <param name="parameters">Parameters used for the simulation.</param>
    /// <param name="cancellationSource">Cancellation token source for stopping the simulation.</param>
    void RegisterSimulation(
        Guid simulationId,
        SimulationType type,
        Dictionary<string, object> parameters,
        CancellationTokenSource cancellationSource);

    /// <summary>
    /// Unregisters a completed or cancelled simulation.
    /// </summary>
    /// <param name="simulationId">The simulation to unregister.</param>
    /// <returns>True if the simulation was found and removed, false otherwise.</returns>
    bool UnregisterSimulation(Guid simulationId);

    /// <summary>
    /// Gets a snapshot of all currently active simulations.
    /// </summary>
    /// <returns>List of active simulation summaries.</returns>
    IReadOnlyList<ActiveSimulationInfo> GetActiveSimulations();

    /// <summary>
    /// Gets the count of currently active simulations.
    /// </summary>
    int ActiveCount { get; }

    /// <summary>
    /// Gets the count of active simulations of a specific type.
    /// </summary>
    /// <param name="type">The simulation type to count.</param>
    /// <returns>Number of active simulations of the specified type.</returns>
    int GetActiveCountByType(SimulationType type);

    /// <summary>
    /// Cancels all active simulations.
    /// </summary>
    /// <returns>Number of simulations that were cancelled.</returns>
    int CancelAll();

    /// <summary>
    /// Cancels all active simulations of a specific type.
    /// </summary>
    /// <param name="type">The simulation type to cancel.</param>
    /// <returns>Number of simulations that were cancelled.</returns>
    int CancelByType(SimulationType type);

    /// <summary>
    /// Tries to get information about a specific simulation.
    /// </summary>
    /// <param name="simulationId">The simulation ID to look up.</param>
    /// <param name="info">The simulation info if found.</param>
    /// <returns>True if the simulation was found, false otherwise.</returns>
    bool TryGetSimulation(Guid simulationId, out ActiveSimulationInfo? info);
}

/// <summary>
/// Event arguments for simulation lifecycle events.
/// </summary>
public class SimulationEventArgs : EventArgs
{
    /// <summary>
    /// Gets the simulation ID.
    /// </summary>
    public Guid SimulationId { get; }

    /// <summary>
    /// Gets the simulation type.
    /// </summary>
    public SimulationType Type { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationEventArgs"/> class.
    /// </summary>
    public SimulationEventArgs(Guid simulationId, SimulationType type)
    {
        SimulationId = simulationId;
        Type = type;
    }
}

/// <summary>
/// Information about an active simulation.
/// </summary>
/// <remarks>
/// This is a public snapshot of simulation state. The internal tracking data
/// includes the <see cref="CancellationTokenSource"/> which is not exposed
/// to prevent external manipulation.
/// </remarks>
public class ActiveSimulationInfo
{
    /// <summary>
    /// Unique identifier for the simulation.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Type of simulation.
    /// </summary>
    public required SimulationType Type { get; init; }

    /// <summary>
    /// When the simulation started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Parameters used for the simulation.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }
}

/// <summary>
/// Thread-safe registry for tracking all active performance simulations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Central registry that tracks all running simulations (CPU stress, memory pressure, slow
/// requests, etc.). This enables the dashboard to show active simulations, the admin panel
/// to cancel them, and the system to prevent conflicting simulations.
/// </para>
/// <para>
/// <strong>KEY FEATURES:</strong>
/// <list type="bullet">
/// <item>Thread-safe concurrent access (multiple simulations can start/stop simultaneously)</item>
/// <item>Cancellation support (each simulation can be stopped individually or all at once)</item>
/// <item>Event notifications (subscribers notified when simulations start/complete)</item>
/// <item>Query capabilities (get active count, filter by type, get details)</item>
/// </list>
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>When simulation starts: RegisterSimulation() stores ID, type, parameters, and cancellation token</item>
/// <item>When simulation ends: UnregisterSimulation() removes entry and fires completion event</item>
/// <item>When admin cancels: CancelByType() or CancelAll() triggers cancellation tokens</item>
/// <item>Dashboard periodically queries GetActiveSimulations() for status display</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Use APCu or Redis for cross-request state (PHP processes don't share memory)</item>
/// <item>Node.js: Use Map or object with event emitter pattern</item>
/// <item>Java: Use ConcurrentHashMap with AtomicReference for thread safety</item>
/// <item>Python: Use threading.Lock with dict, or asyncio task tracking</item>
/// <item>All: Key concept is thread-safe dictionary + cancellation mechanism</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// <list type="bullet">
/// <item>All *Service.cs files - Register/unregister when simulations start/end</item>
/// <item>Controllers/AdminController.cs - Queries active simulations and cancels them</item>
/// <item>Services/MetricsBroadcastService.cs - Listens for start/complete events</item>
/// </list>
/// </para>
/// </remarks>
public class SimulationTracker : ISimulationTracker
{
    private readonly ConcurrentDictionary<Guid, TrackedSimulation> _simulations = new();
    private readonly ILogger<SimulationTracker> _logger;

    /// <inheritdoc />
    public event EventHandler<SimulationEventArgs>? SimulationStarted;

    /// <inheritdoc />
    public event EventHandler<SimulationEventArgs>? SimulationCompleted;

    /// <summary>
    /// Internal tracking record that includes the cancellation source.
    /// </summary>
    private record TrackedSimulation(
        ActiveSimulationInfo Info,
        CancellationTokenSource CancellationSource);

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationTracker"/> class.
    /// </summary>
    /// <param name="logger">Logger for recording simulation lifecycle events.</param>
    public SimulationTracker(ILogger<SimulationTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void RegisterSimulation(
        Guid simulationId,
        SimulationType type,
        Dictionary<string, object> parameters,
        CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(cancellationSource);

        var info = new ActiveSimulationInfo
        {
            Id = simulationId,
            Type = type,
            StartedAt = DateTimeOffset.UtcNow,
            Parameters = parameters.AsReadOnly()
        };

        var tracked = new TrackedSimulation(info, cancellationSource);

        if (_simulations.TryAdd(simulationId, tracked))
        {
            _logger.LogInformation(
                "Registered {SimulationType} simulation {SimulationId} with parameters: {@Parameters}",
                type,
                simulationId,
                parameters);

            // Fire the SimulationStarted event
            SimulationStarted?.Invoke(this, new SimulationEventArgs(simulationId, type));
        }
        else
        {
            _logger.LogWarning(
                "Failed to register simulation {SimulationId} - ID already exists",
                simulationId);
        }
    }

    /// <inheritdoc />
    public bool UnregisterSimulation(Guid simulationId)
    {
        if (_simulations.TryRemove(simulationId, out var tracked))
        {
            _logger.LogInformation(
                "Unregistered {SimulationType} simulation {SimulationId} (ran for {Duration})",
                tracked.Info.Type,
                simulationId,
                DateTimeOffset.UtcNow - tracked.Info.StartedAt);

            // Fire the SimulationCompleted event
            SimulationCompleted?.Invoke(this, new SimulationEventArgs(simulationId, tracked.Info.Type));

            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActiveSimulationInfo> GetActiveSimulations()
    {
        return _simulations.Values
            .Select(t => t.Info)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public int ActiveCount => _simulations.Count;

    /// <inheritdoc />
    public int GetActiveCountByType(SimulationType type) =>
        _simulations.Values.Count(t => t.Info.Type == type);

    /// <inheritdoc />
    public int CancelAll()
    {
        var cancelled = 0;

        foreach (var kvp in _simulations)
        {
            try
            {
                kvp.Value.CancellationSource.Cancel();
                cancelled++;
            }
            catch (ObjectDisposedException)
            {
                // Cancellation source was already disposed, skip
            }
        }

        _logger.LogInformation("Cancelled {Count} active simulations", cancelled);

        // Clear all simulations after cancellation
        _simulations.Clear();

        return cancelled;
    }

    /// <inheritdoc />
    public int CancelByType(SimulationType type)
    {
        var cancelled = 0;
        var toRemove = new List<Guid>();

        foreach (var kvp in _simulations)
        {
            if (kvp.Value.Info.Type == type)
            {
                try
                {
                    kvp.Value.CancellationSource.Cancel();
                    cancelled++;
                    toRemove.Add(kvp.Key);
                }
                catch (ObjectDisposedException)
                {
                    // Cancellation source was already disposed, still remove
                    toRemove.Add(kvp.Key);
                }
            }
        }

        // Remove cancelled simulations
        foreach (var id in toRemove)
        {
            _simulations.TryRemove(id, out _);
        }

        _logger.LogInformation("Cancelled {Count} {Type} simulations", cancelled, type);

        return cancelled;
    }

    /// <inheritdoc />
    public bool TryGetSimulation(Guid simulationId, out ActiveSimulationInfo? info)
    {
        if (_simulations.TryGetValue(simulationId, out var tracked))
        {
            info = tracked.Info;
            return true;
        }

        info = null;
        return false;
    }
}
