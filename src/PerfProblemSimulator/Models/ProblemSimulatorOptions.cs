namespace PerfProblemSimulator.Models;

/// <summary>
/// Configuration options for the Performance Problem Simulator.
/// Loaded from the "ProblemSimulator" section of appsettings.json.
/// </summary>
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
}
