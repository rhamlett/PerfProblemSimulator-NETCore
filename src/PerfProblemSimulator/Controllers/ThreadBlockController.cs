using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for thread pool starvation simulation endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ WARNING: This controller demonstrates a dangerous anti-pattern ⚠️</strong>
/// </para>
/// <para>
/// This controller triggers sync-over-async thread blocking, which causes thread pool
/// starvation. This is one of the most common and difficult-to-diagnose performance
/// problems in ASP.NET applications.
/// </para>
/// <para>
/// <strong>Symptoms of Thread Pool Starvation:</strong>
/// <list type="bullet">
/// <item>All endpoints become slow simultaneously</item>
/// <item>CPU usage is LOW (threads are waiting, not working)</item>
/// <item>Request queue grows continuously</item>
/// <item>Health checks start failing</item>
/// <item>Application appears "hung" but isn't crashed</item>
/// </list>
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ThreadBlockController : ControllerBase
{
    private readonly IThreadBlockService _threadBlockService;
    private readonly ILogger<ThreadBlockController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadBlockController"/> class.
    /// </summary>
    public ThreadBlockController(
        IThreadBlockService threadBlockService,
        ILogger<ThreadBlockController> logger)
    {
        _threadBlockService = threadBlockService ?? throw new ArgumentNullException(nameof(threadBlockService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Triggers sync-over-async thread pool starvation.
    /// </summary>
    /// <param name="request">
    /// Optional request body specifying delay and concurrency. If not provided, defaults are used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request.</param>
    /// <returns>
    /// Information about the started simulation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>What This Does:</strong>
    /// </para>
    /// <para>
    /// This endpoint spawns multiple concurrent operations, each of which BLOCKS a thread pool
    /// thread using the sync-over-async anti-pattern (<c>Task.Delay(ms).Result</c>).
    /// </para>
    /// <para>
    /// While these threads are blocked waiting, they cannot handle any other requests.
    /// If enough threads are blocked, new requests will queue up waiting for threads,
    /// causing the entire application to appear slow or hung.
    /// </para>
    /// <para>
    /// <strong>How to Observe:</strong>
    /// <list type="bullet">
    /// <item><term>dotnet-counters</term><description>
    /// <c>dotnet-counters monitor -p {PID} --counters System.Runtime[threadpool-thread-count,threadpool-queue-length]</c>
    /// </description></item>
    /// <item><term>Try other endpoints</term><description>
    /// While this is running, try calling /api/health - it will be slow!
    /// </description></item>
    /// <item><term>Azure Portal</term><description>
    /// Look at Request Queue Length in App Service metrics
    /// </description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <response code="200">Thread blocking simulation started successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="403">Problem endpoints are disabled via environment variable.</response>
    [HttpPost("trigger-sync-over-async")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TriggerSyncOverAsync(
        [FromBody] ThreadBlockRequest? request,
        CancellationToken cancellationToken)
    {
        var delayMs = request?.DelayMilliseconds ?? 1000;
        var concurrentRequests = request?.ConcurrentRequests ?? 10;

        _logger.LogWarning(
            "⚠️ Received thread blocking request: DelayMs={Delay}, ConcurrentRequests={Concurrent}, ClientIP={ClientIP}",
            delayMs,
            concurrentRequests,
            HttpContext.Connection.RemoteIpAddress);

        try
        {
            var result = await _threadBlockService.TriggerSyncOverAsyncAsync(
                delayMs,
                concurrentRequests,
                cancellationToken);

            _logger.LogWarning(
                "⚠️ Started thread blocking simulation {SimulationId} - THIS WILL CAUSE THREAD STARVATION",
                result.SimulationId);

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Thread blocking request was cancelled by client");
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start thread blocking simulation");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ErrorResponse.SimulationError("Failed to start thread blocking simulation."));
        }
    }
}
