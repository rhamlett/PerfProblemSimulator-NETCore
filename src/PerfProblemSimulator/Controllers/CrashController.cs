using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for triggering intentional application crashes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This controller provides endpoints to intentionally
/// crash the application in various ways. This is useful for learning:
/// </para>
/// <list type="bullet">
/// <item>How to configure Azure App Service crash monitoring</item>
/// <item>How to collect and analyze crash dumps</item>
/// <item>Understanding different types of fatal errors in .NET</item>
/// <item>Practicing crash dump analysis with WinDbg or Visual Studio</item>
/// </list>
/// <para>
/// <strong>WARNING:</strong> These endpoints will terminate the application!
/// Azure App Service will automatically restart the application after a crash.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Crash Simulation")]
public class CrashController : ControllerBase
{
    private readonly ICrashService _crashService;
    private readonly ILogger<CrashController> _logger;

    public CrashController(ICrashService crashService, ILogger<CrashController> logger)
    {
        _crashService = crashService ?? throw new ArgumentNullException(nameof(crashService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Triggers an application crash of the specified type.
    /// </summary>
    /// <param name="request">The crash configuration.</param>
    /// <returns>Confirmation that the crash has been scheduled (response sent before crash occurs).</returns>
    /// <remarks>
    /// <para>
    /// <strong>WARNING:</strong> This endpoint will TERMINATE the application!
    /// </para>
    /// <para>
    /// The response is sent before the crash occurs, so you will receive a 200 OK
    /// indicating the crash has been scheduled. The actual crash happens shortly after.
    /// </para>
    /// <para>
    /// <strong>Azure Crash Monitoring Setup:</strong>
    /// </para>
    /// <list type="number">
    /// <item>In Azure Portal, go to your App Service</item>
    /// <item>Navigate to "Diagnose and solve problems"</item>
    /// <item>Search for "Crash Monitoring"</item>
    /// <item>Enable crash monitoring with "Collect Memory Dump"</item>
    /// <item>Call this endpoint to trigger a crash</item>
    /// <item>Download the crash dump from the Azure portal</item>
    /// </list>
    /// </remarks>
    /// <response code="200">Crash has been scheduled</response>
    /// <response code="400">Invalid crash configuration</response>
    /// <response code="503">Problem endpoints are disabled</response>
    [HttpPost("trigger")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult TriggerCrash([FromBody] CrashRequest? request)
    {
        request ??= new CrashRequest();

        // Validate delay
        if (request.DelaySeconds < 0 || request.DelaySeconds > 60)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "INVALID_DELAY",
                Message = "DelaySeconds must be between 0 and 60"
            });
        }

        _logger.LogCritical(
            "🚨 CRASH REQUESTED: Type={CrashType}, Delay={DelaySeconds}s, Synchronous={Synchronous}",
            request.CrashType, request.DelaySeconds, request.Synchronous);

        // If synchronous, crash immediately (no response will be sent)
        if (request.Synchronous)
        {
            _crashService.TriggerCrash(request.CrashType, 0, request.Message, synchronous: true);
            // This line is never reached - the process crashes above
            return StatusCode(500, "Crash failed");
        }

        // Trigger the crash asynchronously
        _crashService.TriggerCrash(request.CrashType, request.DelaySeconds, request.Message, synchronous: false);

        var crashMessage = $"💥 {request.CrashType} crash scheduled! " +
                      (request.DelaySeconds > 0 
                          ? $"Crash will occur in {request.DelaySeconds} seconds." 
                          : "Crash will occur in ~100ms (after this response is sent).");

        return Ok(new SimulationResult
        {
            SimulationId = Guid.NewGuid(),
            Type = SimulationType.Crash,
            Status = "Scheduled",
            Message = crashMessage,
            ActualParameters = new Dictionary<string, object>
            {
                ["CrashType"] = request.CrashType.ToString(),
                ["DelaySeconds"] = request.DelaySeconds,
                ["Synchronous"] = request.Synchronous
            }
        });
    }

    /// <summary>
    /// Triggers a synchronous crash optimized for Azure Crash Monitoring.
    /// </summary>
    /// <param name="crashType">Type of crash (default: FailFast)</param>
    /// <returns>No response - the process crashes before responding.</returns>
    /// <remarks>
    /// <para>
    /// <strong>USE THIS ENDPOINT FOR AZURE CRASH MONITORING!</strong>
    /// </para>
    /// <para>
    /// This endpoint crashes the process during the HTTP request, before any response is sent.
    /// This is the most reliable way to trigger Azure Crash Monitoring to capture a dump.
    /// </para>
    /// <para>
    /// The browser/client will receive a connection error because no response is sent.
    /// </para>
    /// </remarks>
    /// <response code="500">Never returned - process crashes before response</response>
    [HttpGet("now")]
    [HttpPost("now")]
    public IActionResult CrashNow([FromQuery] CrashType crashType = CrashType.FailFast)
    {
        _logger.LogCritical("🚨💥 IMMEDIATE CRASH REQUESTED: Type={CrashType} - NO RESPONSE WILL BE SENT", crashType);
        
        _crashService.TriggerCrash(crashType, 0, "Immediate crash via /api/crash/now endpoint", synchronous: true);
        
        // Never reached
        return StatusCode(500, "Crash failed");
    }

    /// <summary>
    /// Gets information about available crash types.
    /// </summary>
    /// <returns>A dictionary of crash types and their descriptions.</returns>
    /// <remarks>
    /// Use this endpoint to understand what each crash type does before triggering it.
    /// </remarks>
    /// <response code="200">List of crash types and descriptions</response>
    [HttpGet("types")]
    [ProducesResponseType(typeof(Dictionary<string, CrashTypeInfo>), StatusCodes.Status200OK)]
    public IActionResult GetCrashTypes()
    {
        var descriptions = _crashService.GetCrashTypeDescriptions();
        
        var result = descriptions.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => new CrashTypeInfo
            {
                Name = kvp.Key.ToString(),
                Value = (int)kvp.Key,
                Description = kvp.Value,
                RecommendedForAzure = kvp.Key == CrashType.FailFast || kvp.Key == CrashType.StackOverflow
            });

        return Ok(result);
    }

    /// <summary>
    /// Triggers a FailFast crash (most common for Azure crash monitoring).
    /// </summary>
    /// <param name="delaySeconds">Optional delay before crash (0-60 seconds).</param>
    /// <returns>Confirmation that the crash has been scheduled.</returns>
    /// <remarks>
    /// This is a convenience endpoint for the most commonly used crash type.
    /// Environment.FailFast is the recommended method for testing Azure crash monitoring.
    /// </remarks>
    /// <response code="200">Crash has been scheduled</response>
    /// <response code="503">Problem endpoints are disabled</response>
    [HttpPost("failfast")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult TriggerFailFast([FromQuery] int delaySeconds = 0)
    {
        return TriggerCrash(new CrashRequest
        {
            CrashType = CrashType.FailFast,
            DelaySeconds = Math.Clamp(delaySeconds, 0, 60),
            Message = "FailFast triggered via /api/crash/failfast endpoint"
        });
    }

    /// <summary>
    /// Triggers a StackOverflow crash.
    /// </summary>
    /// <param name="delaySeconds">Optional delay before crash (0-60 seconds).</param>
    /// <returns>Confirmation that the crash has been scheduled.</returns>
    /// <remarks>
    /// Creates interesting stack traces for analysis. The crash dump will show
    /// repeated calls to the same method, demonstrating infinite recursion.
    /// </remarks>
    /// <response code="200">Crash has been scheduled</response>
    /// <response code="503">Problem endpoints are disabled</response>
    [HttpPost("stackoverflow")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult TriggerStackOverflow([FromQuery] int delaySeconds = 0)
    {
        return TriggerCrash(new CrashRequest
        {
            CrashType = CrashType.StackOverflow,
            DelaySeconds = Math.Clamp(delaySeconds, 0, 60)
        });
    }
}

/// <summary>
/// Information about a crash type.
/// </summary>
public class CrashTypeInfo
{
    /// <summary>
    /// Name of the crash type.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Numeric value for the crash type enum.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Detailed description of what this crash type does.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Whether this crash type is recommended for Azure crash monitoring.
    /// </summary>
    public bool RecommendedForAzure { get; set; }
}
