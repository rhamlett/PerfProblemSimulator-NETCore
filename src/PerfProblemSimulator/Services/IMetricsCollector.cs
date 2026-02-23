using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for collecting system metrics on a dedicated thread.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong> Provides real-time system metrics (CPU, memory, thread pool)
/// for the dashboard. Runs on a DEDICATED THREAD (not the thread pool) to ensure metrics
/// are collected even during thread pool starvation scenarios.
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// Key concept: This must NOT use the normal async/thread pool because thread pool starvation
/// is one of the problems we demonstrate. Use a dedicated OS-level thread.
/// <list type="bullet">
/// <item>PHP: Use pcntl_fork() or separate process (PHP shares-nothing model)</item>
/// <item>Node.js: Use Worker threads (worker_threads module) - NOT the event loop</item>
/// <item>Java: new Thread() with custom Runnable, NOT ExecutorService pool</item>
/// <item>Python: threading.Thread() - but note Python GIL limits true parallelism</item>
/// <item>Ruby: Thread.new - but consider limitations of MRI GIL</item>
/// </list>
/// Metric collection APIs by language:
/// <list type="bullet">
/// <item>PHP: sys_getloadavg(), memory_get_usage()</item>
/// <item>Node.js: os.cpus(), process.memoryUsage(), require('v8').getHeapStatistics()</item>
/// <item>Java: OperatingSystemMXBean, MemoryMXBean, ThreadMXBean</item>
/// <item>Python: psutil.cpu_percent(), psutil.virtual_memory(), threading.active_count()</item>
/// <item>Ruby: Sys::CPU (sys-cpu gem), GetProcessMem</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// MetricsCollector.cs (implementation), MetricsBroadcastService.cs (SignalR bridge),
/// Models/MetricsSnapshot.cs (data structure)
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
