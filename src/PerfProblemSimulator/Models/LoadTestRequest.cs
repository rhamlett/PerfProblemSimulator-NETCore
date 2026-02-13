/*
 * =============================================================================
 * LOAD TEST REQUEST MODEL - AI PORTABILITY DOCUMENTATION
 * =============================================================================
 * 
 * PURPOSE:
 * Defines the input parameters for load test requests. All parameters have
 * sensible defaults, making the request body optional.
 * 
 * JSON EXAMPLE:
 * {
 *     "workIterations": 1000,
 *     "bufferSizeKb": 100,
 *     "baselineDelayMs": 500,
 *     "softLimit": 5,
 *     "degradationFactor": 200
 * }
 * 
 * OR with defaults (empty body or null):
 * {}
 * 
 * PORTING TO OTHER LANGUAGES:
 * - PHP: class LoadTestRequest { public int $workIterations = 1000; ... }
 * - Node.js: interface LoadTestRequest { workIterations?: number; ... }
 * - Java: public class LoadTestRequest { private int workIterations = 1000; ... }
 * - Python: @dataclass class LoadTestRequest: work_iterations: int = 1000
 * 
 * =============================================================================
 */

namespace PerfProblemSimulator.Models;

/// <summary>
/// Request parameters for load test endpoint.
/// All properties have defaults, making the request body optional.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PARAMETER TUNING GUIDE:</strong>
/// </para>
/// <list type="bullet">
/// <item>
/// <term>workIterations</term>
/// <description>
/// Higher values = more CPU time per request. Start with 1000 and adjust
/// based on your instance size. On a Basic B1, 1000 iterations takes ~5-10ms.
/// </description>
/// </item>
/// <item>
/// <term>bufferSizeKb</term>
/// <description>
/// Memory allocated per request. Released after request completes.
/// 100KB provides meaningful memory pressure during load testing.
/// </description>
/// </item>
/// <item>
/// <term>softLimit</term>
/// <description>
/// Concurrent requests before degradation starts. Lower = earlier degradation.
/// Default of 5 ensures rapid escalation under load.
/// </description>
/// </item>
/// <item>
/// <term>degradationFactor</term>
/// <description>
/// Milliseconds of delay added per request OVER the soft limit.
/// Default of 200ms creates steep degradation curve.
/// 
/// Example: softLimit=5, degradationFactor=200
/// - 10 concurrent: (10-5) * 200 = 1000ms added delay
/// - 20 concurrent: (20-5) * 200 = 3000ms added delay
/// - 50 concurrent: (50-5) * 200 = 9000ms added delay
/// </description>
/// </item>
/// <item>
/// <term>baselineDelayMs</term>
/// <description>
/// Minimum blocking delay for every request. Default 500ms ensures
/// thread pool exhaustion under any significant load.
/// </description>
/// </item>
/// </list>
/// </remarks>
public class LoadTestRequest
{
    /*
     * =========================================================================
     * DEFAULT VALUES
     * =========================================================================
     * 
     * These defaults are designed to create ~100ms baseline response time
     * on a typical Azure App Service instance (B1/S1).
     * 
     * Adjust based on your target environment:
     * - Larger instances: Increase workIterations for similar response times
     * - Smaller instances: Decrease workIterations to avoid excessive load
     */
    
    /// <summary>
    /// Number of SHA256 hash iterations to perform for CPU work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 1000</strong>
    /// </para>
    /// <para>
    /// Each iteration computes a SHA256 hash. The output of each hash
    /// becomes the input for the next iteration, preventing compiler
    /// optimization.
    /// </para>
    /// <para>
    /// <strong>PERFORMANCE REFERENCE:</strong>
    /// <list type="bullet">
    /// <item>1000 iterations ≈ 5-10ms on B1/S1</item>
    /// <item>5000 iterations ≈ 25-50ms on B1/S1</item>
    /// <item>10000 iterations ≈ 50-100ms on B1/S1</item>
    /// </list>
    /// </para>
    /// </remarks>
    public int WorkIterations { get; set; } = 1000;

    /// <summary>
    /// Size of memory buffer to allocate in kilobytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 100 KB</strong>
    /// </para>
    /// <para>
    /// This memory is allocated at the start of request processing and
    /// released when the request completes (garbage collected).
    /// </para>
    /// <para>
    /// The buffer is "touched" (written to and read from) to ensure actual
    /// memory allocation occurs and isn't optimized away.
    /// </para>
    /// </remarks>
    public int BufferSizeKb { get; set; } = 100;

    /// <summary>
    /// Number of concurrent requests before degradation delays begin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 5</strong>
    /// </para>
    /// <para>
    /// When concurrent requests exceed this limit, additional delay is
    /// added proportional to how far over the limit we are.
    /// </para>
    /// <para>
    /// <strong>SOFT LIMIT CONCEPT:</strong>
    /// Unlike a hard limit (which would reject requests), a soft limit
    /// gracefully degrades performance. This mimics real application
    /// behavior where resources become contended under load.
    /// </para>
    /// <para>
    /// <strong>TUNING GUIDE:</strong>
    /// <list type="bullet">
    /// <item>Lower softLimit = Earlier degradation, better for testing thresholds</item>
    /// <item>Higher softLimit = Later degradation, requires more load to see effects</item>
    /// </list>
    /// </para>
    /// </remarks>
    public int SoftLimit { get; set; } = 5;

    /// <summary>
    /// Milliseconds of delay added per concurrent request over the soft limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 200 ms</strong>
    /// </para>
    /// <para>
    /// <strong>DEGRADATION FORMULA:</strong>
    /// <code>
    /// additionalDelay = max(0, currentConcurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// <para>
    /// <strong>EXAMPLES (softLimit=5, degradationFactor=200):</strong>
    /// <list type="bullet">
    /// <item>5 concurrent → 0ms added (at soft limit)</item>
    /// <item>10 concurrent → 1000ms added ((10-5) × 200)</item>
    /// <item>20 concurrent → 3000ms added ((20-5) × 200)</item>
    /// <item>50 concurrent → 9000ms added ((50-5) × 200)</item>
    /// <item>100 concurrent → 19000ms added ((100-5) × 200)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>REACHING 230s TIMEOUT:</strong>
    /// To reach Azure's 230s timeout with these defaults:
    /// (230000ms - 500ms baseline) / 200ms = ~1147 requests over soft limit
    /// So: 5 + 1147 = ~1152 concurrent requests to timeout
    /// </para>
    /// </remarks>
    public int DegradationFactor { get; set; } = 200;

    /// <summary>
    /// Minimum blocking delay applied to every request in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 500 ms</strong>
    /// </para>
    /// <para>
    /// This baseline delay is applied BEFORE the degradation calculation.
    /// It ensures every request blocks a thread for at least this duration,
    /// guaranteeing thread pool exhaustion under any significant load.
    /// </para>
    /// <para>
    /// Combined with degradation factor, total delay is:
    /// <code>
    /// totalDelay = baselineDelayMs + max(0, concurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// </remarks>
    public int BaselineDelayMs { get; set; } = 500;
}
