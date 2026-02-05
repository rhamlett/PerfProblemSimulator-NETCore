using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for retrieving current metrics and health status.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This controller provides REST endpoints for polling metrics.
/// While the dashboard uses SignalR for real-time updates, these endpoints
/// are useful for:
/// </para>
/// <list type="bullet">
/// <item>External monitoring systems that can't use WebSockets</item>
/// <item>Scripted health checks (curl, PowerShell, etc.)</item>
/// <item>Debugging and manual testing</item>
/// <item>Integration with Azure App Service health probes</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[RequestTimeout("NoTimeout")] // Metrics endpoints must always respond
public class MetricsController : ControllerBase
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<MetricsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsController"/> class.
    /// </summary>
    public MetricsController(IMetricsCollector metricsCollector, ILogger<MetricsController> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the latest metrics snapshot.
    /// </summary>
    /// <remarks>
    /// Returns the most recent metrics collected by the background service.
    /// This is a lightweight cached read, not a live calculation.
    /// </remarks>
    /// <returns>The latest <see cref="MetricsSnapshot"/>.</returns>
    /// <response code="200">Returns the current metrics snapshot.</response>
    [HttpGet("current")]
    [ProducesResponseType(typeof(MetricsSnapshot), StatusCodes.Status200OK)]
    public ActionResult<MetricsSnapshot> GetCurrentMetrics()
    {
        _logger.LogDebug("Current metrics requested via REST API");
        var snapshot = _metricsCollector.LatestSnapshot;
        return Ok(snapshot);
    }

    /// <summary>
    /// Gets detailed health status including warnings.
    /// </summary>
    /// <remarks>
    /// Returns comprehensive health information including active simulations,
    /// resource usage, and any warning conditions detected.
    /// </remarks>
    /// <returns>Detailed application health status.</returns>
    /// <response code="200">Returns the detailed health status.</response>
    [HttpGet("health")]
    [ProducesResponseType(typeof(ApplicationHealthStatus), StatusCodes.Status200OK)]
    public ActionResult<ApplicationHealthStatus> GetDetailedHealth()
    {
        _logger.LogDebug("Detailed health status requested via REST API");
        var status = _metricsCollector.GetHealthStatus();
        return Ok(status);
    }
}
