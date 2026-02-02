namespace PerfProblemSimulator.Models;

/// <summary>
/// Request model for starting slow request simulation.
/// </summary>
public class SlowRequestRequest
{
    /// <summary>
    /// Approximate duration for each slow request in seconds.
    /// Default: 25 seconds (ideal for 60-second CLR Profile capture).
    /// </summary>
    public int RequestDurationSeconds { get; set; } = 25;

    /// <summary>
    /// Interval between spawning new slow requests in seconds.
    /// Default: 10 seconds.
    /// </summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of requests to send. 0 = unlimited until stopped.
    /// Default: 0 (unlimited).
    /// </summary>
    public int MaxRequests { get; set; } = 0;
}

/// <summary>
/// The type of slow request scenario to simulate.
/// </summary>
public enum SlowRequestScenario
{
    /// <summary>
    /// Randomly selects from all available scenarios.
    /// </summary>
    Random,

    /// <summary>
    /// Simple sync-over-async with .Result and .Wait() calls.
    /// Profiler shows: Time blocked at Task.Result and Task.Wait().
    /// </summary>
    SimpleSyncOverAsync,

    /// <summary>
    /// Nested sync-over-async with multiple layers of blocking.
    /// Profiler shows: Chain of blocking calls through multiple methods.
    /// </summary>
    NestedSyncOverAsync,

    /// <summary>
    /// Realistic database/HTTP pattern with GetAwaiter().GetResult().
    /// Profiler shows: Common pattern found in legacy code migrations.
    /// </summary>
    DatabasePattern
}
