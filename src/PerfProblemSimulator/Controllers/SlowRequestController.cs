using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for slow request simulation to demonstrate CLR Profiler diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This controller simulates slow requests that are ideal
/// for CLR Profiler analysis. Unlike CPU or memory issues, sync-over-async problems show:
/// </para>
/// <list type="bullet">
/// <item>Low CPU usage (threads are waiting, not working)</item>
/// <item>Normal memory usage</item>
/// <item>Slow response times</item>
/// <item>Threads blocked at .Result, .Wait(), or GetAwaiter().GetResult()</item>
/// </list>
/// <para>
/// <strong>How to Use:</strong>
/// </para>
/// <list type="number">
/// <item>Start the slow request simulation</item>
/// <item>In Azure Portal: Diagnose and Solve Problems → Collect a .NET Profiler Trace</item>
/// <item>Or use dotnet-trace: <c>dotnet-trace collect -p {PID} --duration 00:01:00</c></item>
/// <item>Analyze the trace to find threads blocked in sync-over-async patterns</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Slow Request Simulation")]
public class SlowRequestController : ControllerBase
{
    private readonly ISlowRequestService _slowRequestService;
    private readonly ILogger<SlowRequestController> _logger;

    public SlowRequestController(
        ISlowRequestService slowRequestService,
        ILogger<SlowRequestController> logger)
    {
        _slowRequestService = slowRequestService ?? throw new ArgumentNullException(nameof(slowRequestService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the slow request simulation.
    /// </summary>
    /// <param name="request">Configuration for the simulation.</param>
    /// <returns>Information about the started simulation.</returns>
    /// <remarks>
    /// <para>
    /// This starts a background process that spawns slow HTTP-like requests at regular intervals.
    /// Each request randomly uses one of three sync-over-async patterns:
    /// </para>
    /// <list type="bullet">
    /// <item><strong>SimpleSyncOverAsync</strong>: Direct .Result and .Wait() calls</item>
    /// <item><strong>NestedSyncOverAsync</strong>: Chain of sync methods that block internally</item>
    /// <item><strong>DatabasePattern</strong>: Realistic GetAwaiter().GetResult() pattern</item>
    /// </list>
    /// <para>
    /// <strong>Recommended Settings for CLR Profile (60s default):</strong>
    /// </para>
    /// <list type="bullet">
    /// <item>RequestDurationSeconds: 25 (each request takes ~25s)</item>
    /// <item>IntervalSeconds: 10 (new request every 10s)</item>
    /// </list>
    /// </remarks>
    /// <response code="200">Simulation started successfully</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    public IActionResult Start([FromBody] SlowRequestRequest? request)
    {
        request ??= new SlowRequestRequest();

        _logger.LogWarning(
            "🐌 Starting slow request simulation: Duration={Duration}s, Interval={Interval}s",
            request.RequestDurationSeconds,
            request.IntervalSeconds);

        var result = _slowRequestService.Start(request);
        return Ok(result);
    }

    /// <summary>
    /// Stops the slow request simulation.
    /// </summary>
    /// <returns>Summary of the simulation run.</returns>
    /// <remarks>
    /// Stops spawning new requests. Requests already in progress will complete.
    /// </remarks>
    /// <response code="200">Simulation stopped</response>
    [HttpPost("stop")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    public IActionResult Stop()
    {
        _logger.LogInformation("🛑 Stopping slow request simulation");
        var result = _slowRequestService.Stop();
        return Ok(result);
    }

    /// <summary>
    /// Gets the current status of the slow request simulation.
    /// </summary>
    /// <returns>Current simulation status including request counts.</returns>
    /// <response code="200">Current status</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(SlowRequestStatus), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var status = _slowRequestService.GetStatus();
        return Ok(status);
    }

    /// <summary>
    /// Gets information about the slow request scenarios.
    /// </summary>
    /// <returns>Description of each scenario and what to look for in CLR Profiler.</returns>
    /// <response code="200">Scenario information</response>
    [HttpGet("scenarios")]
    [ProducesResponseType(typeof(Dictionary<string, ScenarioInfo>), StatusCodes.Status200OK)]
    public IActionResult GetScenarios()
    {
        var scenarios = new Dictionary<string, ScenarioInfo>
        {
            ["SimpleSyncOverAsync"] = new ScenarioInfo
            {
                Name = "Simple Sync-Over-Async",
                Description = "Direct .Result and .Wait() calls on async methods",
                WhatProfilerShows = "Time blocked at Task.Result, Task.Wait(), and ManualResetEventSlim.Wait",
                MethodsToLookFor = new[]
                {
                    "FetchDataAsync_BLOCKING_HERE",
                    "ProcessDataAsync_BLOCKING_HERE",
                    "SaveDataAsync_BLOCKING_HERE"
                }
            },
            ["NestedSyncOverAsync"] = new ScenarioInfo
            {
                Name = "Nested Sync-Over-Async",
                Description = "Chain of sync methods that each block on async internally",
                WhatProfilerShows = "Nested blocking calls - sync methods calling other sync methods that block",
                MethodsToLookFor = new[]
                {
                    "ValidateOrderSync_BLOCKS_INTERNALLY",
                    "CheckInventorySync_BLOCKS_INTERNALLY",
                    "ProcessPaymentSync_BLOCKS_INTERNALLY",
                    "SendConfirmationSync_BLOCKS_INTERNALLY"
                }
            },
            ["DatabasePattern"] = new ScenarioInfo
            {
                Name = "Database/HTTP Pattern",
                Description = "GetAwaiter().GetResult() pattern common in legacy code migrations",
                WhatProfilerShows = "Multiple GetAwaiter().GetResult() calls simulating database and HTTP calls",
                MethodsToLookFor = new[]
                {
                    "GetCustomerFromDatabaseAsync_SYNC_BLOCK",
                    "GetOrderHistoryFromDatabaseAsync_SYNC_BLOCK",
                    "CheckInventoryServiceAsync_SYNC_BLOCK",
                    "GetRecommendationsFromMLServiceAsync_SYNC_BLOCK",
                    "BuildResponseAsync_SYNC_BLOCK"
                }
            }
        };

        return Ok(scenarios);
    }
}

/// <summary>
/// Information about a slow request scenario.
/// </summary>
public class ScenarioInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string WhatProfilerShows { get; set; } = "";
    public string[] MethodsToLookFor { get; set; } = Array.Empty<string>();
}
