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
 *     "bufferSizeKb": 5,
 *     "softLimit": 50,
 *     "degradationFactor": 5
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
/// 5KB is lightweight; increase for memory pressure testing.
/// </description>
/// </item>
/// <item>
/// <term>softLimit</term>
/// <description>
/// Concurrent requests before degradation starts. Lower = earlier degradation.
/// Tune based on expected normal load. 50 is good for typical web apps.
/// </description>
/// </item>
/// <item>
/// <term>degradationFactor</term>
/// <description>
/// Milliseconds of delay added per request OVER the soft limit.
/// Higher = steeper degradation curve. 5ms is moderate.
/// 
/// Example: softLimit=50, degradationFactor=5
/// - 60 concurrent: (60-50) * 5 = 50ms added delay
/// - 100 concurrent: (100-50) * 5 = 250ms added delay
/// - 200 concurrent: (200-50) * 5 = 750ms added delay
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
    /// <strong>DEFAULT: 5 KB</strong>
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
    public int BufferSizeKb { get; set; } = 5;

    /// <summary>
    /// Number of concurrent requests before degradation delays begin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 50</strong>
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
    public int SoftLimit { get; set; } = 50;

    /// <summary>
    /// Milliseconds of delay added per concurrent request over the soft limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 5 ms</strong>
    /// </para>
    /// <para>
    /// <strong>DEGRADATION FORMULA:</strong>
    /// <code>
    /// additionalDelay = max(0, currentConcurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// <para>
    /// <strong>EXAMPLES (softLimit=50, degradationFactor=5):</strong>
    /// <list type="bullet">
    /// <item>30 concurrent → 0ms added (below soft limit)</item>
    /// <item>60 concurrent → 50ms added ((60-50) × 5)</item>
    /// <item>100 concurrent → 250ms added ((100-50) × 5)</item>
    /// <item>200 concurrent → 750ms added ((200-50) × 5)</item>
    /// <item>500 concurrent → 2250ms added ((500-50) × 5)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>REACHING 230s TIMEOUT:</strong>
    /// To reach Azure's 230s timeout with these defaults:
    /// (230000ms - 100ms base) / 5ms = 45980 requests over soft limit
    /// So: 50 + 45980 = ~46000 concurrent requests
    /// 
    /// For faster degradation, increase degradationFactor:
    /// - degradationFactor=50: ~4650 concurrent requests to timeout
    /// - degradationFactor=100: ~2350 concurrent requests to timeout
    /// </para>
    /// </remarks>
    public int DegradationFactor { get; set; } = 5;
}
