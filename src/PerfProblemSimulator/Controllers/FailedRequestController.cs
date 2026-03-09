using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for generating failed HTTP requests (5xx responses).
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// This controller provides endpoints to generate HTTP 500 errors for testing
/// Azure diagnostics tools like AppLens and Application Insights.
/// </para>
/// <para>
/// <strong>USE CASES:</strong>
/// <list type="bullet">
/// <item>Testing AppLens error detection and analysis</item>
/// <item>Validating Application Insights failure alerts</item>
/// <item>Testing error rate based auto-scaling rules</item>
/// <item>Training developers on error diagnosis in Azure</item>
/// </list>
/// </para>
/// <para>
/// <strong>HOW IT WORKS:</strong>
/// The service makes HTTP requests to the /api/loadtest endpoint with parameters
/// configured for guaranteed failure (100% error probability). The load test endpoint
/// throws a random exception, resulting in an HTTP 500 response. Each request takes
/// approximately 1.5 seconds, ensuring the failures appear in latency monitoring.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Failed Request Simulation")]
public class FailedRequestController : ControllerBase
{
    private readonly IFailedRequestService _failedRequestService;
    private readonly ILogger<FailedRequestController> _logger;

    public FailedRequestController(
        IFailedRequestService failedRequestService,
        ILogger<FailedRequestController> logger)
    {
        _failedRequestService = failedRequestService ?? throw new ArgumentNullException(nameof(failedRequestService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts generating failed HTTP requests.
    /// </summary>
    /// <param name="request">Configuration specifying how many failures to generate.</param>
    /// <returns>Information about the started simulation.</returns>
    /// <remarks>
    /// <para>
    /// This endpoint starts a background process that generates HTTP 500 errors.
    /// Each failed request takes approximately 1.5 seconds and throws a random
    /// exception type (TimeoutException, NullReferenceException, etc.).
    /// </para>
    /// <para>
    /// <strong>Where to see the failures:</strong>
    /// <list type="bullet">
    /// <item>Azure Portal → App Service → Diagnose and Solve Problems → AppLens</item>
    /// <item>Application Insights → Failures blade</item>
    /// <item>Azure Monitor → Alerts (if error rate rules configured)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <response code="200">Simulation started successfully</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    public IActionResult Start([FromBody] FailedRequestRequest? request)
    {
        var requestCount = request?.RequestCount ?? 10;

        _logger.LogWarning(
            "❌ Starting failed request simulation: Count={Count}",
            requestCount);

        var result = _failedRequestService.Start(requestCount);
        return Ok(result);
    }

    /// <summary>
    /// Stops the failed request simulation.
    /// </summary>
    /// <returns>Summary of the simulation run.</returns>
    /// <remarks>
    /// Stops generating new failed requests. Requests already in progress will complete.
    /// </remarks>
    /// <response code="200">Simulation stopped</response>
    [HttpPost("stop")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    public IActionResult Stop()
    {
        _logger.LogInformation("🛑 Stopping failed request simulation");
        var result = _failedRequestService.Stop();
        return Ok(result);
    }

    /// <summary>
    /// Gets the current status of the failed request simulation.
    /// </summary>
    /// <returns>Current simulation status including request counts.</returns>
    /// <response code="200">Current status</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(FailedRequestStatus), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var status = _failedRequestService.GetStatus();
        return Ok(status);
    }
}

/// <summary>
/// Request model for starting a failed request simulation.
/// </summary>
public class FailedRequestRequest
{
    /// <summary>
    /// Number of failed requests (HTTP 500s) to generate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>DEFAULT: 10</strong>
    /// </para>
    /// <para>
    /// Each request takes approximately 1.5 seconds before failing.
    /// Requests are spread out with a small delay between them to ensure
    /// they appear as distinct data points in monitoring tools.
    /// </para>
    /// </remarks>
    public int RequestCount { get; set; } = 10;
}
