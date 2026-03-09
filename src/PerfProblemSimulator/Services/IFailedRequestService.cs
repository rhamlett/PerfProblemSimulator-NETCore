using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service interface for generating failed HTTP requests (5xx responses).
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// This service generates HTTP 500 errors that will appear in Azure diagnostics tools
/// like AppLens and Application Insights. It does this by making requests to the load
/// test endpoint with parameters configured for guaranteed failure.
/// </para>
/// <para>
/// <strong>USE CASES:</strong>
/// <list type="bullet">
/// <item>Testing Azure AppLens error detection and analysis</item>
/// <item>Validating Application Insights failure alerts</item>
/// <item>Training developers on error diagnosis workflows</item>
/// <item>Testing error rate based auto-scaling rules</item>
/// </list>
/// </para>
/// <para>
/// <strong>HOW IT WORKS:</strong>
/// <list type="number">
/// <item>Spawns a background thread to generate requests</item>
/// <item>Each request calls /api/loadtest with 100% error probability</item>
/// <item>The load test endpoint throws a random exception, resulting in HTTP 500</item>
/// <item>Requests include enough work time (~1.5s) to appear in latency monitoring</item>
/// </list>
/// </para>
/// </remarks>
public interface IFailedRequestService
{
    /// <summary>
    /// Starts generating failed HTTP requests.
    /// </summary>
    /// <param name="requestCount">Number of failed requests to generate.</param>
    /// <returns>Information about the started simulation.</returns>
    SimulationResult Start(int requestCount);

    /// <summary>
    /// Stops the failed request simulation.
    /// </summary>
    /// <returns>Summary of the simulation run.</returns>
    SimulationResult Stop();

    /// <summary>
    /// Gets the current status of the failed request simulation.
    /// </summary>
    FailedRequestStatus GetStatus();

    /// <summary>
    /// Gets whether the simulation is currently running.
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Status information for the failed request simulation.
/// </summary>
public class FailedRequestStatus
{
    /// <summary>
    /// Whether the simulation is currently running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Number of failed requests that have been sent.
    /// </summary>
    public int RequestsSent { get; set; }

    /// <summary>
    /// Number of requests that completed (with expected 5xx error).
    /// </summary>
    public int RequestsCompleted { get; set; }

    /// <summary>
    /// Number of requests currently in progress.
    /// </summary>
    public int RequestsInProgress { get; set; }

    /// <summary>
    /// Target number of failed requests to generate.
    /// </summary>
    public int TargetCount { get; set; }

    /// <summary>
    /// When the simulation started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }
}
