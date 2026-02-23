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
/// <item>Environment variables (highest priority, e.g., ProblemSimulator__AppTitle)</item>
/// <item>appsettings.{Environment}.json (Development, Production)</item>
/// <item>appsettings.json (base settings)</item>
/// <item>Default values in this class (lowest priority)</item>
/// </list>
/// </para>
/// <para>
/// <strong>AZURE APP SERVICE CONFIGURATION:</strong>
/// In Azure Portal > App Service > Configuration > Application settings:
/// - Name: ProblemSimulator__AppTitle
/// - Value: "My Custom Title"
/// The double underscore (__) translates to colon (:) for nested JSON properties.
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
    /// The title displayed in the dashboard header.
    /// </summary>
    /// <remarks>
    /// Can be overridden via Azure App Service configuration using
    /// the environment variable: ProblemSimulator__AppTitle
    /// </remarks>
    public string AppTitle { get; set; } = "Performance Problem Simulator";
}
