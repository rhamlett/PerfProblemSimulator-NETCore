/*
 * =============================================================================
 * LOAD TEST SERVICE INTERFACE - AI PORTABILITY DOCUMENTATION
 * =============================================================================
 * 
 * PURPOSE:
 * Defines the contract for the load test service. In languages without interfaces
 * (like Python, JavaScript), this serves as documentation for what methods 
 * your implementation must provide.
 * 
 * PORTING TO OTHER LANGUAGES:
 * - PHP: interface ILoadTestService { ... } or just implement directly
 * - Node.js: Document expected methods; TypeScript can use interface
 * - Java: interface ILoadTestService { ... } (exactly like C#)
 * - Python: Use ABC (Abstract Base Class) or Protocol, or just document
 * 
 * INTERFACE PATTERN BENEFITS:
 * 1. Testability - Can create mock implementations for unit tests
 * 2. Swappability - Can change implementation without changing consumers
 * 3. Documentation - Clearly defines expected behavior
 * 
 * =============================================================================
 */

using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for the load test service that handles request work simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>IMPLEMENTATION NOTES:</strong>
/// </para>
/// <para>
/// The implementing class must:
/// <list type="bullet">
/// <item>Track concurrent requests using thread-safe counter</item>
/// <item>Calculate degradation delay based on soft limit</item>
/// <item>Perform lightweight CPU and memory work</item>
/// <item>Throw random exceptions after 120s with 20% probability</item>
/// </list>
/// </para>
/// <para>
/// <strong>THREAD SAFETY REQUIREMENTS:</strong>
/// The concurrent request counter must be thread-safe because multiple
/// requests will increment/decrement it simultaneously.
/// </para>
/// <para>
/// <strong>PORTING GUIDANCE:</strong>
/// <list type="bullet">
/// <item>
/// <term>PHP</term>
/// <description>Use static class variables with mutex/semaphore, or Redis/APCu for shared state</description>
/// </item>
/// <item>
/// <term>Node.js</term>
/// <description>Single-threaded, but use atomic operations if using worker threads</description>
/// </item>
/// <item>
/// <term>Java</term>
/// <description>Use AtomicInteger for concurrent counter</description>
/// </item>
/// <item>
/// <term>Python</term>
/// <description>Use threading.Lock or asyncio.Lock for concurrent access</description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public interface ILoadTestService
{
    /*
     * =========================================================================
     * PRIMARY METHOD: Execute Work
     * =========================================================================
     * 
     * This is the main method that performs the load test work.
     * 
     * EXPECTED BEHAVIOR:
     * 1. Increment concurrent request counter (thread-safe)
     * 2. Start timing
     * 3. Calculate degradation delay if over soft limit
     * 4. Perform CPU work (hash iterations)
     * 5. Allocate memory buffer (released on completion)
     * 6. Check for 120s timeout and 20% exception chance
     * 7. Decrement concurrent request counter (in finally block)
     * 8. Return result with timing details
     * 
     * ASYNC PATTERN:
     * Returns Task<T> for async/await pattern. In languages without native
     * async, this could be synchronous or use promises/futures.
     */

    /// <summary>
    /// Executes the load test work with the specified parameters.
    /// </summary>
    /// <param name="request">Configuration for the load test behavior.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result containing timing and diagnostic information.</returns>
    Task<LoadTestResult> ExecuteWorkAsync(LoadTestRequest request, CancellationToken cancellationToken);

    /*
     * =========================================================================
     * STATISTICS METHOD: Get Current Stats
     * =========================================================================
     * 
     * Returns current state without performing work.
     * Useful for monitoring dashboards and health checks.
     */

    /// <summary>
    /// Gets current load test statistics without performing any work.
    /// </summary>
    /// <returns>Current statistics including concurrent request count.</returns>
    LoadTestStats GetCurrentStats();
}

/*
 * =============================================================================
 * STATISTICS RECORD
 * =============================================================================
 * 
 * C# RECORDS:
 * A "record" is an immutable data class with value-based equality.
 * Think of it as a simple data container.
 * 
 * PORTING:
 * - PHP: class LoadTestStats { public int $currentConcurrentRequests; ... }
 * - Node.js: { currentConcurrentRequests: number, ... } (plain object or class)
 * - Java: record LoadTestStats(int currentConcurrentRequests, ...) {} or POJO
 * - Python: @dataclass class LoadTestStats: current_concurrent_requests: int
 */

/// <summary>
/// Current statistics for the load test service.
/// </summary>
/// <param name="CurrentConcurrentRequests">Number of requests currently being processed.</param>
/// <param name="TotalRequestsProcessed">Total requests processed since app start.</param>
/// <param name="TotalExceptionsThrown">Total random exceptions thrown (after 120s timeout).</param>
/// <param name="AverageResponseTimeMs">Average response time in milliseconds.</param>
public record LoadTestStats(
    int CurrentConcurrentRequests,
    long TotalRequestsProcessed,
    long TotalExceptionsThrown,
    double AverageResponseTimeMs
);
