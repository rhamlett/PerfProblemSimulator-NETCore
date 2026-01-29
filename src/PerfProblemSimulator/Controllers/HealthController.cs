using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for application health checks.
/// Provides endpoints that remain responsive even under stress conditions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> Health endpoints are critical for load balancers,
/// orchestrators (like Kubernetes), and monitoring systems. Azure App Service uses
/// health check endpoints to determine if an instance should receive traffic.
/// </para>
/// <para>
/// This controller is designed to remain responsive even during performance problem
/// simulations. It provides both a simple liveness probe (/api/health) and a more
/// detailed status endpoint (/api/health/status) that includes information about
/// active simulations.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthController"/> class.
    /// </summary>
    /// <param name="simulationTracker">Service for tracking active simulations.</param>
    /// <param name="logger">Logger for health check events.</param>
    public HealthController(
        ISimulationTracker simulationTracker,
        ILogger<HealthController> logger)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Simple liveness probe endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a simple "Healthy" response to indicate the application is running.
    /// This endpoint should always respond quickly, regardless of system load.
    /// </para>
    /// <para>
    /// <strong>Azure App Service Usage:</strong> Configure this as the health probe path
    /// in your App Service configuration to enable automatic instance replacement
    /// when the application becomes unresponsive.
    /// </para>
    /// </remarks>
    /// <response code="200">Application is healthy and responding to requests.</response>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new HealthResponse
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Detailed status endpoint including active simulation information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides more detailed health information including the count and types
    /// of any currently running simulations. Useful for monitoring dashboards
    /// that need to understand the current state of the simulator.
    /// </para>
    /// </remarks>
    /// <response code="200">Returns detailed health status.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(DetailedHealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var activeSimulations = _simulationTracker.GetActiveSimulations();

        return Ok(new DetailedHealthResponse
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow,
            ActiveSimulationCount = activeSimulations.Count,
            ActiveSimulations = activeSimulations
                .Select(s => new ActiveSimulationSummary
                {
                    Id = s.Id,
                    Type = s.Type.ToString(),
                    StartedAt = s.StartedAt,
                    RunningDurationSeconds = (int)(DateTimeOffset.UtcNow - s.StartedAt).TotalSeconds
                })
                .ToList()
        });
    }
}

/// <summary>
/// Simple health check response.
/// </summary>
public class HealthResponse
{
    /// <summary>
    /// Health status. Always "Healthy" if the endpoint responds.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When this health check was performed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Detailed health check response including simulation state.
/// </summary>
public class DetailedHealthResponse : HealthResponse
{
    /// <summary>
    /// Number of currently active simulations.
    /// </summary>
    public int ActiveSimulationCount { get; init; }

    /// <summary>
    /// Summary of each active simulation.
    /// </summary>
    public List<ActiveSimulationSummary> ActiveSimulations { get; init; } = [];
}

/// <summary>
/// Summary information about an active simulation.
/// </summary>
public class ActiveSimulationSummary
{
    /// <summary>
    /// Unique identifier for the simulation.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Type of simulation (Cpu, Memory, ThreadBlock).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// When the simulation started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// How long the simulation has been running in seconds.
    /// </summary>
    public int RunningDurationSeconds { get; init; }
}
