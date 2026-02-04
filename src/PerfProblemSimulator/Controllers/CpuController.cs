using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for CPU stress simulation endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ WARNING: This controller triggers intentional performance problems ⚠️</strong>
/// </para>
/// <para>
/// This controller provides endpoints to trigger high CPU usage for educational and
/// diagnostic purposes. It should only be used in controlled environments for:
/// <list type="bullet">
/// <item>Learning how to diagnose high CPU issues</item>
/// <item>Testing monitoring and alerting systems</item>
/// <item>Practicing with diagnostic tools (dotnet-counters, dotnet-trace, etc.)</item>
/// <item>Validating auto-scaling configurations</item>
/// </list>
/// </para>
/// <para>
/// <strong>Safety Features:</strong>
/// <list type="bullet">
/// <item>Duration is capped to a configurable maximum (default: 300 seconds)</item>
/// <item>Can be disabled entirely via DISABLE_PROBLEM_ENDPOINTS environment variable</item>
/// <item>All operations are logged for audit purposes</item>
/// <item>Simulations can be cancelled via the admin endpoint or by restarting the app</item>
/// </list>
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CpuController : ControllerBase
{
    private readonly ICpuStressService _cpuStressService;
    private readonly ILogger<CpuController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuController"/> class.
    /// </summary>
    /// <param name="cpuStressService">Service for triggering CPU stress.</param>
    /// <param name="logger">Logger for request tracking.</param>
    public CpuController(
        ICpuStressService cpuStressService,
        ILogger<CpuController> logger)
    {
        _cpuStressService = cpuStressService ?? throw new ArgumentNullException(nameof(cpuStressService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Triggers a high CPU usage simulation.
    /// </summary>
    /// <param name="request">
    /// Optional request body specifying the duration. If not provided, defaults are used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request.</param>
    /// <returns>
    /// Information about the started simulation, including its ID and actual parameters.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>What This Does:</strong>
    /// </para>
    /// <para>
    /// This endpoint starts a CPU stress operation that will consume all available CPU cores
    /// for the specified duration. The stress is created using parallel spin loops - a classic
    /// anti-pattern that wastes CPU cycles doing nothing useful.
    /// </para>
    /// <para>
    /// <strong>How to Observe:</strong>
    /// <list type="bullet">
    /// <item><term>Task Manager</term><description>Watch the CPU graph spike to near 100%</description></item>
    /// <item><term>dotnet-counters</term><description><c>dotnet-counters monitor -p {PID}</c></description></item>
    /// <item><term>Azure Portal</term><description>App Service → Metrics → CPU Percentage</description></item>
    /// <item><term>Application Insights</term><description>Performance → Server CPU</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Request Example:</strong>
    /// <code>
    /// POST /api/cpu/trigger-high-cpu
    /// Content-Type: application/json
    ///
    /// { "durationSeconds": 60 }
    /// </code>
    /// </para>
    /// </remarks>
    /// <response code="200">CPU stress simulation started successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="403">Problem endpoints are disabled via environment variable.</response>
    [HttpPost("trigger-high-cpu")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TriggerHighCpu(
        [FromBody] CpuStressRequest? request,
        CancellationToken cancellationToken)
    {
        // Use defaults if no request body provided
        var durationSeconds = request?.DurationSeconds ?? 30;
        var targetPercentage = request?.TargetPercentage ?? 100;

        // Log the incoming request (FR-010: Request logging)
        _logger.LogInformation(
            "Received CPU stress request: DurationSeconds={Duration}, Percentage={Percentage}, ClientIP={ClientIP}",
            durationSeconds,
            targetPercentage,
            HttpContext.Connection.RemoteIpAddress);

        try
        {
            var result = await _cpuStressService.TriggerCpuStressAsync(durationSeconds, cancellationToken, targetPercentage);

            _logger.LogInformation(
                "Started CPU stress simulation {SimulationId} for {Duration}s @ {Percentage}%",
                result.SimulationId,
                result.ActualParameters?["DurationSeconds"],
                result.ActualParameters?["TargetPercentage"]);

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CPU stress request was cancelled by client");
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start CPU stress simulation");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ErrorResponse.SimulationError("Failed to start CPU stress simulation. See server logs for details."));
        }
    }
}
