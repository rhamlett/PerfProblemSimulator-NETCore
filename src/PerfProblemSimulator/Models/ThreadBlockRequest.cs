using System.ComponentModel.DataAnnotations;

namespace PerfProblemSimulator.Models;

/// <summary>
/// Request model for triggering sync-over-async thread pool starvation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> Sync-over-async is one of the most common and
/// insidious anti-patterns in ASP.NET applications. It occurs when synchronous code
/// blocks on an async operation, consuming a thread pool thread while waiting.
/// </para>
/// <para>
/// When many requests do this simultaneously, the thread pool becomes exhausted,
/// causing all requests (even simple ones) to queue up waiting for threads.
/// </para>
/// </remarks>
public class ThreadBlockRequest
{
    /// <summary>
    /// How long each blocking operation should delay in milliseconds.
    /// </summary>
    /// <remarks>
    /// Default: 1000ms (1 second). This represents an async operation like an HTTP call
    /// or database query that is being blocked on synchronously.
    /// </remarks>
    [Range(100, int.MaxValue, ErrorMessage = "Delay must be at least 100ms")]
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Number of concurrent blocking operations to trigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: 10. This simulates multiple requests all blocking synchronously at once.
    /// </para>
    /// <para>
    /// The default thread pool minimum is typically min(workerThreads, 8) per core.
    /// Setting this higher than available threads demonstrates starvation.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "Concurrent requests must be at least 1")]
    public int ConcurrentRequests { get; set; } = 10;
}
