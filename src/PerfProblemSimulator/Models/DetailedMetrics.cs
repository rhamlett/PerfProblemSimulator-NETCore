namespace PerfProblemSimulator.Models;

/// <summary>
/// Detailed CPU metrics for the health status endpoint.
/// </summary>
public class CpuMetrics
{
    /// <summary>
    /// Current CPU usage percentage (0-100).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Number of logical processors available.
    /// </summary>
    public int ProcessorCount { get; init; }
}

/// <summary>
/// Detailed memory metrics for the health status endpoint.
/// </summary>
public class MemoryMetrics
{
    /// <summary>
    /// Process working set in bytes.
    /// </summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>
    /// Managed GC heap size in bytes.
    /// </summary>
    public long GcHeapBytes { get; init; }

    /// <summary>
    /// Number of intentionally allocated memory blocks.
    /// </summary>
    public int AllocatedBlocksCount { get; init; }

    /// <summary>
    /// Total size of intentionally allocated blocks in bytes.
    /// </summary>
    public long AllocatedBlocksTotalBytes { get; init; }
}

/// <summary>
/// Detailed thread pool metrics for the health status endpoint.
/// </summary>
public class ThreadPoolMetrics
{
    /// <summary>
    /// Current number of thread pool threads.
    /// </summary>
    public int ThreadCount { get; init; }

    /// <summary>
    /// Number of work items waiting in the thread pool queue.
    /// </summary>
    public long PendingWorkItems { get; init; }

    /// <summary>
    /// Total work items completed since application start.
    /// </summary>
    public long CompletedWorkItems { get; init; }

    /// <summary>
    /// Number of available worker threads.
    /// </summary>
    public int AvailableWorkerThreads { get; init; }

    /// <summary>
    /// Maximum worker threads in the pool.
    /// </summary>
    public int MaxWorkerThreads { get; init; }

    /// <summary>
    /// Number of available I/O completion threads.
    /// </summary>
    public int AvailableIoThreads { get; init; }

    /// <summary>
    /// Maximum I/O completion threads in the pool.
    /// </summary>
    public int MaxIoThreads { get; init; }
}

/// <summary>
/// Comprehensive application health status.
/// </summary>
public class ApplicationHealthStatus
{
    /// <summary>
    /// When this status was captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// CPU-related metrics.
    /// </summary>
    public CpuMetrics? Cpu { get; init; }

    /// <summary>
    /// Memory-related metrics.
    /// </summary>
    public MemoryMetrics? Memory { get; init; }

    /// <summary>
    /// Thread pool statistics.
    /// </summary>
    public ThreadPoolMetrics? ThreadPool { get; init; }

    /// <summary>
    /// Summary of currently active simulations.
    /// </summary>
    public List<SimulationSummary> ActiveSimulations { get; init; } = [];

    /// <summary>
    /// Overall health assessment.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Any warning messages about current state.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Summary of an active simulation for metrics reporting.
/// </summary>
public class SimulationSummary
{
    /// <summary>
    /// Simulation identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Type of simulation.
    /// </summary>
    public SimulationType Type { get; init; }

    /// <summary>
    /// When the simulation started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// How long the simulation has been running.
    /// </summary>
    public TimeSpan RunningDuration { get; init; }
}
