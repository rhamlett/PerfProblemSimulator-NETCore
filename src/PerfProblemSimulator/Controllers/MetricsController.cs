using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// REST API endpoints for retrieving current metrics and health status.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Provides HTTP polling endpoints as an alternative to the real-time SignalR/WebSocket
/// channel. While the dashboard uses SignalR for live updates, these REST endpoints serve:
/// </para>
/// <list type="bullet">
/// <item>External monitoring systems that can't use WebSockets (Azure Monitor, Prometheus scrapers)</item>
/// <item>Scripted health checks (curl, PowerShell, automation pipelines)</item>
/// <item>Manual debugging and testing via browser or API tools</item>
/// <item>Integration with Azure App Service health probes</item>
/// </list>
/// <para>
/// <strong>DESIGN DECISION - REST vs SignalR:</strong>
/// The dashboard UI uses SignalR because it needs sub-second updates for visualizing
/// performance problems in real-time. These REST endpoints return cached data (not live
/// calculations) and are suitable for 5-30 second polling intervals.
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Standard REST controller returning JSON</item>
/// <item>Node.js/Express: app.get('/api/metrics/current', ...)</item>
/// <item>Java/Spring: @RestController with @GetMapping</item>
/// <item>Python/Flask: @app.route('/api/metrics/current')</item>
/// <item>Ruby/Rails: Standard controller action rendering JSON</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// <list type="bullet">
/// <item>Services/MetricsCollector.cs - Provides the cached metrics data</item>
/// <item>Models/MetricsSnapshot.cs - Data structure returned</item>
/// <item>Hubs/MetricsHub.cs - Real-time alternative (SignalR)</item>
/// </list>
/// </para>
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
