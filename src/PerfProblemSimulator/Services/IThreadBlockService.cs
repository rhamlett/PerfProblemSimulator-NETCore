using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for the thread blocking service that triggers sync-over-async starvation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This service demonstrates the sync-over-async
/// anti-pattern, which is one of the most common causes of thread pool starvation
/// in ASP.NET applications.
/// </para>
/// <para>
/// <strong>What is sync-over-async?</strong>
/// It's when code uses <c>.Result</c>, <c>.Wait()</c>, or <c>.GetAwaiter().GetResult()</c>
/// on a Task instead of properly using <c>await</c>. This blocks the current thread
/// while waiting for the async operation to complete.
/// </para>
/// </remarks>
public interface IThreadBlockService
{
    /// <summary>
    /// Triggers sync-over-async thread blocking to demonstrate thread pool starvation.
    /// </summary>
    /// <param name="delayMilliseconds">
    /// How long each blocking operation delays. Will be capped to the configured maximum.
    /// </param>
    /// <param name="concurrentRequests">
    /// Number of concurrent blocking operations. Will be capped to the configured maximum.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to request early cancellation.
    /// </param>
    /// <returns>
    /// A result containing the simulation ID and actual parameters used.
    /// </returns>
    Task<SimulationResult> TriggerSyncOverAsyncAsync(
        int delayMilliseconds,
        int concurrentRequests,
        CancellationToken cancellationToken);
}
