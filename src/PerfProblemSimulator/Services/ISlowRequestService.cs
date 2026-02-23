using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service interface for simulating slow HTTP requests that cause thread pool starvation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// This service generates slow HTTP requests to demonstrate thread pool starvation and
/// its impact on application responsiveness. It's designed to work with the Azure CLR
/// Profiler to help developers learn to diagnose blocking calls in production.
/// </para>
/// <para>
/// <strong>WHY THIS SIMULATION EXISTS:</strong>
/// <list type="bullet">
/// <item>Thread pool starvation is a common production issue in .NET/Java/Node applications</item>
/// <item>Symptoms are confusing: everything "works" but response times spike to 30+ seconds</item>
/// <item>Diagnosis requires profiler tools that most developers haven't used</item>
/// <item>This simulation creates reproducible starvation for training purposes</item>
/// </list>
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>Spawn a background thread that generates HTTP requests at a configurable interval</item>
/// <item>Each HTTP request goes through the web server pipeline (uses thread pool thread)</item>
/// <item>The request handler intentionally sleeps for 20-25 seconds (blocking the thread)</item>
/// <item>After enough concurrent slow requests, all thread pool threads are blocked</item>
/// <item>New requests (including health probes) must wait for threads, causing latency spikes</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Not directly applicable (PHP uses process-per-request model)</item>
/// <item>Node.js: Use blocking operations in async handlers to demonstrate event loop blocking</item>
/// <item>Java: Create threads that block on synchronized methods or Thread.sleep()</item>
/// <item>Python: Use time.sleep() in Flask/Django request handlers with limited worker threads</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// <list type="bullet">
/// <item>Services/SlowRequestService.cs - Implementation</item>
/// <item>Controllers/SlowRequestController.cs - REST API endpoints</item>
/// <item>Models/SlowRequestRequest.cs - Configuration model</item>
/// </list>
/// </para>
/// </remarks>
public interface ISlowRequestService
{
    /// <summary>
    /// Starts the slow request simulation.
    /// </summary>
    SimulationResult Start(SlowRequestRequest request);

    /// <summary>
    /// Stops the slow request simulation.
    /// </summary>
    SimulationResult Stop();

    /// <summary>
    /// Gets the current status of the slow request simulation.
    /// </summary>
    SlowRequestStatus GetStatus();

    /// <summary>
    /// Gets whether the simulation is currently running.
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Status information for the slow request simulation.
/// </summary>
public class SlowRequestStatus
{
    public bool IsRunning { get; set; }
    public int RequestsSent { get; set; }
    public int RequestsCompleted { get; set; }
    public int RequestsInProgress { get; set; }
    public int IntervalSeconds { get; set; }
    public int RequestDurationSeconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public Dictionary<string, int> ScenarioCounts { get; set; } = new();
}
