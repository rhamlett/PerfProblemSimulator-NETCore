namespace PerfProblemSimulator.Models;

/// <summary>
/// Lightweight metrics snapshot for real-time SignalR updates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This is a <c>readonly record struct</c> for maximum
/// efficiency when broadcasting to many SignalR clients. Value types avoid heap allocations,
/// and the readonly modifier ensures immutability for thread-safe access.
/// </para>
/// </remarks>
public readonly record struct MetricsSnapshot
{
    /// <summary>
    /// When this snapshot was captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Current CPU usage percentage (0-100).
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Process working set in megabytes.
    /// </summary>
    public double WorkingSetMb { get; init; }

    /// <summary>
    /// GC heap size in megabytes.
    /// </summary>
    public double GcHeapMb { get; init; }

    /// <summary>
    /// Current thread pool thread count.
    /// </summary>
    public int ThreadPoolThreads { get; init; }

    /// <summary>
    /// Number of work items waiting in the thread pool queue.
    /// </summary>
    public long ThreadPoolQueueLength { get; init; }

    /// <summary>
    /// Number of currently active simulations.
    /// </summary>
    public int ActiveSimulationCount { get; init; }
}
