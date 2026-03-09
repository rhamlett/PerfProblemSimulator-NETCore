namespace PerfProblemSimulator.Models;

/// <summary>
/// Application-wide configuration options loaded from appsettings.json or environment variables.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Centralized configuration that can be customized per deployment environment.
/// Follows the Options pattern for dependency injection.
/// </para>
/// <para>
/// <strong>CONFIGURATION SOURCES (in priority order):</strong>
/// <list type="number">
/// <item>Environment variables (highest priority)</item>
/// <item>appsettings.{Environment}.json (Development, Production)</item>
/// <item>appsettings.json (base settings)</item>
/// <item>Default values in this class (lowest priority)</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Use $_ENV or .env files with vlucas/phpdotenv</item>
/// <item>Node.js: Use dotenv package and process.env</item>
/// <item>Java/Spring: Use @ConfigurationProperties or application.properties</item>
/// <item>Python: Use python-dotenv and os.environ</item>
/// </list>
/// </para>
/// </remarks>
public class ProblemSimulatorOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ProblemSimulator";

    /// <summary>
    /// How often the metrics collector should sample system metrics in milliseconds.
    /// </summary>
    /// <remarks>
    /// Default: 1000 ms (1 second). Faster collection provides more responsive
    /// dashboard updates but consumes more resources.
    /// </remarks>
    public int MetricsCollectionIntervalMs { get; set; } = 1000;

    /// <summary>
    /// How often the latency probe service should send HTTP probes in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: 200ms (5 probes/sec). Combined with client-side probing at the same
    /// interval but offset by half, this achieves ~100ms effective sample rate.
    /// </para>
    /// <para>
    /// <strong>Safety limit:</strong> Minimum 100ms (10 probes/sec). Values below 100ms
    /// will be clamped to 100ms to prevent probe overlap and excessive CPU overhead.
    /// </para>
    /// <para>
    /// Lower values provide finer granularity for detecting latency spikes but increase
    /// CPU overhead and can cause probe overlap under heavy profiling. Adjust based on
    /// profiler feedback.
    /// </para>
    /// <para>
    /// Can be configured via environment variable: <c>HEALTH_PROBE_RATE</c>
    /// </para>
    /// </remarks>
    public int LatencyProbeIntervalMs { get; set; } = 200;
}
