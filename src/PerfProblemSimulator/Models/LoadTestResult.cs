/*
 * =============================================================================
 * LOAD TEST RESULT MODEL - AI PORTABILITY DOCUMENTATION
 * =============================================================================
 * 
 * PURPOSE:
 * Defines the response format for load test requests. Contains timing
 * information, diagnostic details, and request outcome.
 * 
 * JSON EXAMPLE (success):
 * {
 *     "elapsedMs": 157,
 *     "concurrentRequestsAtStart": 73,
 *     "degradationDelayAppliedMs": 115,
 *     "workIterationsCompleted": 1000,
 *     "memoryAllocatedBytes": 5120,
 *     "workCompleted": true,
 *     "exceptionThrown": false,
 *     "exceptionType": null,
 *     "timestamp": "2026-02-12T15:30:45.123Z"
 * }
 * 
 * JSON EXAMPLE (exception after timeout):
 * {
 *     "elapsedMs": 125432,
 *     "concurrentRequestsAtStart": 500,
 *     "degradationDelayAppliedMs": 2250,
 *     "workIterationsCompleted": 0,
 *     "memoryAllocatedBytes": 0,
 *     "workCompleted": false,
 *     "exceptionThrown": true,
 *     "exceptionType": "TimeoutException",
 *     "timestamp": "2026-02-12T15:32:50.555Z"
 * }
 * 
 * PORTING TO OTHER LANGUAGES:
 * - PHP: class LoadTestResult { public int $elapsedMs; ... }
 * - Node.js: interface LoadTestResult { elapsedMs: number; ... }
 * - Java: public class LoadTestResult { private long elapsedMs; ... }
 * - Python: @dataclass class LoadTestResult: elapsed_ms: int
 * 
 * =============================================================================
 */

namespace PerfProblemSimulator.Models;

/// <summary>
/// Result returned from load test endpoint containing timing and diagnostic information.
/// </summary>
/// <remarks>
/// <para>
/// <strong>DIAGNOSTIC VALUE:</strong>
/// </para>
/// <para>
/// This response provides detailed information for analyzing load test results:
/// </para>
/// <list type="bullet">
/// <item>
/// <term>ElapsedMs</term>
/// <description>Total request duration - primary metric for load testing analysis</description>
/// </item>
/// <item>
/// <term>ConcurrentRequestsAtStart</term>
/// <description>Shows load level - correlate with ElapsedMs to see degradation curve</description>
/// </item>
/// <item>
/// <term>DegradationDelayAppliedMs</term>
/// <description>How much artificial delay was added due to soft limit</description>
/// </item>
/// </list>
/// </remarks>
public class LoadTestResult
{
    /// <summary>
    /// Total elapsed time for the request in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the primary metric for load testing analysis. It includes:
    /// <list type="bullet">
    /// <item>Degradation delay (if concurrent requests exceeded soft limit)</item>
    /// <item>CPU work time (hash iterations)</item>
    /// <item>Memory allocation time</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>EXPECTED VALUES:</strong>
    /// <list type="bullet">
    /// <item>Low load: ~100ms (mostly CPU work)</item>
    /// <item>Moderate load: 200-500ms</item>
    /// <item>High load: 1000-5000ms</item>
    /// <item>Extreme load: approaching 230000ms (Azure timeout)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Number of concurrent requests when this request started processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value is captured at request start (after incrementing the counter).
    /// Use this to correlate response time with load level.
    /// </para>
    /// <para>
    /// <strong>ANALYSIS TIP:</strong>
    /// Plot ElapsedMs vs ConcurrentRequestsAtStart to visualize the
    /// degradation curve. You should see linear increase above the soft limit.
    /// </para>
    /// </remarks>
    public int ConcurrentRequestsAtStart { get; set; }

    /// <summary>
    /// Milliseconds of artificial delay applied due to exceeding soft limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>FORMULA:</strong>
    /// <code>
    /// delay = max(0, concurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// <para>
    /// If this value is 0, the request was below the soft limit and only
    /// took time for actual CPU/memory work.
    /// </para>
    /// <para>
    /// <strong>BREAKDOWN:</strong>
    /// <code>
    /// ElapsedMs ≈ DegradationDelayAppliedMs + BaseWorkTime
    /// </code>
    /// Where BaseWorkTime is typically ~100ms for default parameters.
    /// </para>
    /// </remarks>
    public int DegradationDelayAppliedMs { get; set; }

    /// <summary>
    /// Number of hash iterations completed (0 if exception thrown before completion).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This equals the requested WorkIterations if work completed successfully.
    /// Will be 0 if an exception was thrown during processing.
    /// </para>
    /// </remarks>
    public int WorkIterationsCompleted { get; set; }

    /// <summary>
    /// Bytes of memory allocated (0 if exception thrown before allocation).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This equals BufferSizeKb × 1024 if work completed successfully.
    /// The memory is released after the request completes.
    /// </para>
    /// </remarks>
    public int MemoryAllocatedBytes { get; set; }

    /// <summary>
    /// Whether the request completed all work successfully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>true</strong>: All CPU work and memory operations completed.
    /// <strong>false</strong>: An exception was thrown during processing.
    /// </para>
    /// <para>
    /// Use this to calculate success rate during load tests:
    /// <code>
    /// successRate = count(workCompleted == true) / totalRequests
    /// </code>
    /// </para>
    /// </remarks>
    public bool WorkCompleted { get; set; }

    /// <summary>
    /// Whether an exception was thrown during processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exceptions are thrown with 20% probability after request has been
    /// processing for more than 120 seconds.
    /// </para>
    /// <para>
    /// <strong>NOTE:</strong>
    /// If this is true, the HTTP response was still 500 (Internal Server Error).
    /// This field is only populated in successful responses for debugging.
    /// </para>
    /// </remarks>
    public bool ExceptionThrown { get; set; }

    /// <summary>
    /// Type name of exception thrown (null if no exception).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains the .NET exception type name (e.g., "TimeoutException",
    /// "NullReferenceException") for diagnostic purposes.
    /// </para>
    /// <para>
    /// <strong>POSSIBLE VALUES:</strong>
    /// InvalidOperationException, ArgumentException, NullReferenceException,
    /// TimeoutException, IOException, FormatException, DivideByZeroException,
    /// IndexOutOfRangeException, KeyNotFoundException, HttpRequestException,
    /// TaskCanceledException, OutOfMemoryException, StackOverflowException
    /// </para>
    /// </remarks>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// UTC timestamp when the result was generated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for time-series analysis to correlate load test results
    /// with Azure Monitor metrics and Application Insights data.
    /// </para>
    /// </remarks>
    public DateTimeOffset Timestamp { get; set; }
}
