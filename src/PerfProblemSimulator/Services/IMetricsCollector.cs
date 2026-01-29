using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for collecting system metrics on a dedicated thread.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This service runs on a dedicated thread (not the
/// thread pool) to ensure metrics are collected even when the thread pool is starved.
/// This is critical for monitoring because thread pool starvation is one of the problems
/// we're simulating.
/// </para>
/// </remarks>
public interface IMetricsCollector : IDisposable
{
    /// <summary>
    /// Gets the latest collected metrics snapshot.
    /// </summary>
    MetricsSnapshot LatestSnapshot { get; }

    /// <summary>
    /// Gets comprehensive application health status.
    /// </summary>
    ApplicationHealthStatus GetHealthStatus();

    /// <summary>
    /// Event raised when new metrics are collected.
    /// </summary>
    event EventHandler<MetricsSnapshot>? MetricsCollected;

    /// <summary>
    /// Starts the metrics collection thread.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the metrics collection thread.
    /// </summary>
    void Stop();
}
