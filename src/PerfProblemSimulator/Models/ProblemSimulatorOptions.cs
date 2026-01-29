namespace PerfProblemSimulator.Models;

/// <summary>
/// Configuration options for the Performance Problem Simulator.
/// Loaded from the "ProblemSimulator" section of appsettings.json.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> These limits are crucial for preventing the simulator
/// from accidentally causing permanent damage to the host system. In production applications,
/// similar limits should be applied to any resource-intensive operations.
/// </para>
/// <para>
/// The options pattern (<c>IOptions&lt;ProblemSimulatorOptions&gt;</c>) allows these values
/// to be easily overridden per environment (development vs production) and supports
/// hot reloading via <c>IOptionsMonitor&lt;T&gt;</c>.
/// </para>
/// </remarks>
public class ProblemSimulatorOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ProblemSimulator";

    /// <summary>
    /// Maximum duration in seconds for CPU stress simulation.
    /// Prevents runaway CPU consumption.
    /// </summary>
    /// <remarks>
    /// Default: 300 seconds (5 minutes). This provides enough time to observe metrics
    /// and practice diagnosis without risking long-term system instability.
    /// </remarks>
    public int MaxCpuDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum memory that can be allocated in megabytes.
    /// Prevents the application from consuming all available system memory.
    /// </summary>
    /// <remarks>
    /// Default: 1024 MB (1 GB). This is enough to cause visible memory pressure
    /// on most systems without triggering out-of-memory conditions.
    /// </remarks>
    public int MaxMemoryAllocationMb { get; set; } = 1024;

    /// <summary>
    /// Maximum delay in milliseconds for thread blocking simulation.
    /// Limits how long individual threads can be blocked.
    /// </summary>
    /// <remarks>
    /// Default: 30000 ms (30 seconds). Long enough to demonstrate thread pool
    /// starvation effects without permanently blocking threads.
    /// </remarks>
    public int MaxThreadBlockDelayMs { get; set; } = 30000;

    /// <summary>
    /// Maximum number of concurrent blocking requests.
    /// Limits thread pool exhaustion severity.
    /// </summary>
    /// <remarks>
    /// Default: 200. This is typically enough to exhaust the default thread pool
    /// on most systems while remaining manageable.
    /// </remarks>
    public int MaxConcurrentBlockingRequests { get; set; } = 200;

    /// <summary>
    /// How often the metrics collector should sample system metrics in milliseconds.
    /// </summary>
    /// <remarks>
    /// Default: 1000 ms (1 second). Faster collection provides more responsive
    /// dashboard updates but consumes more resources.
    /// </remarks>
    public int MetricsCollectionIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Whether to log detailed information about simulation requests.
    /// </summary>
    /// <remarks>
    /// Default: true. Useful for understanding what's happening during simulations.
    /// May be disabled in production-like testing scenarios for performance.
    /// </remarks>
    public bool EnableRequestLogging { get; set; } = true;
}
