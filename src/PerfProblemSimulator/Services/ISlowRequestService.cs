using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service interface for slow request simulation.
/// </summary>
public interface ISlowRequestService
{
    /// <summary>
    /// Starts the slow request simulation.
    /// </summary>
    SimulationResult Start(SlowRequestRequest request);

    /// <summary>
    /// Stops the slow request simulation.
    /// </summary>
    SimulationResult Stop();

    /// <summary>
    /// Gets the current status of the slow request simulation.
    /// </summary>
    SlowRequestStatus GetStatus();

    /// <summary>
    /// Gets whether the simulation is currently running.
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Status information for the slow request simulation.
/// </summary>
public class SlowRequestStatus
{
    public bool IsRunning { get; set; }
    public int RequestsSent { get; set; }
    public int RequestsCompleted { get; set; }
    public int RequestsInProgress { get; set; }
    public int IntervalSeconds { get; set; }
    public int RequestDurationSeconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public Dictionary<string, int> ScenarioCounts { get; set; } = new();
}
