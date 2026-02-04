using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for slow request simulation to demonstrate CLR Profiler diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This controller simulates slow requests that are ideal
/// for CLR Profiler analysis. Unlike CPU or memory issues, blocking problems show:
/// </para>
/// <list type="bullet">
/// <item>Low CPU usage (threads are blocked/sleeping, not working)</item>
/// <item>Normal memory usage</item>
/// <item>Slow response times</item>
/// <item>Time spent in Thread.Sleep visible in profiler call stacks</item>
/// </list>
/// <para>
/// <strong>How to Use:</strong>
/// </para>
/// <list type="number">
/// <item>Start the slow request simulation</item>
/// <item>In Azure Portal: Diagnose and Solve Problems → Collect a .NET Profiler Trace</item>
/// <item>Or use dotnet-trace: <c>dotnet-trace collect -p {PID} --duration 00:01:00</c></item>
/// <item>Analyze the trace to find blocking methods by their descriptive names</item>
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
    /// This starts a background process that spawns slow HTTP requests at regular intervals.
    /// Each request randomly uses one of three blocking patterns:
    /// </para>
    /// <list type="bullet">
    /// <item><strong>SimpleSyncOverAsync</strong>: Direct Thread.Sleep blocking calls</item>
    /// <item><strong>NestedSyncOverAsync</strong>: Chain of sync methods that block internally</item>
    /// <item><strong>DatabasePattern</strong>: Simulated database/HTTP blocking calls</item>
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
                Name = "Simple Blocking",
                Description = "Direct Thread.Sleep blocking calls that consume time in profiler",
                WhatProfilerShows = "Time spent in Thread.Sleep - clearly visible as method self-time",
                MethodsToLookFor = new[]
                {
                    "FetchDataSync_BLOCKING_HERE",
                    "ProcessDataSync_BLOCKING_HERE",
                    "SaveDataSync_BLOCKING_HERE"
                }
            },
            ["NestedSyncOverAsync"] = new ScenarioInfo
            {
                Name = "Nested Blocking Methods",
                Description = "Chain of sync methods that each block internally using Thread.Sleep",
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
                Description = "Simulated database and HTTP blocking calls",
                WhatProfilerShows = "Time spent in methods simulating database queries and HTTP calls",
                MethodsToLookFor = new[]
                {
                    "GetCustomerFromDatabaseSync_SYNC_BLOCK",
                    "GetOrderHistoryFromDatabaseSync_SYNC_BLOCK",
                    "CheckInventoryServiceSync_SYNC_BLOCK",
                    "GetRecommendationsFromMLServiceSync_SYNC_BLOCK",
                    "BuildResponseSync_SYNC_BLOCK"
                }
            }
        };

        return Ok(scenarios);
    }

    /// <summary>
    /// HTTP endpoint that simulates a slow blocking request.
    /// </summary>
    /// <param name="durationSeconds">How long the request should take (default: 25 seconds).</param>
    /// <param name="scenario">The scenario name for logging (optional).</param>
    /// <returns>Result after the blocking delay completes.</returns>
    /// <remarks>
    /// <para>
    /// <strong>⚠️ This endpoint intentionally blocks!</strong>
    /// </para>
    /// <para>
    /// This endpoint is designed to be called during slow request simulation so that
    /// slow requests show up in the Request Latency Monitor chart. It uses Thread.Sleep
    /// to block the ASP.NET thread pool thread.
    /// </para>
    /// </remarks>
    /// <response code="200">Request completed after blocking</response>
    [HttpGet("execute-slow")]
    [ProducesResponseType(typeof(SlowRequestResult), StatusCodes.Status200OK)]
    public IActionResult ExecuteSlowRequest(
        [FromQuery] int durationSeconds = 25,
        [FromQuery] string? scenario = null)
    {
        var startTime = DateTimeOffset.UtcNow;
        
        _logger.LogWarning("🐌 HTTP slow request started: {Duration}s, Scenario: {Scenario}", 
            durationSeconds, scenario ?? "Direct");
        
        // Execute the appropriate blocking pattern based on the scenario
        // Using "Sync-Over-Async" pattern (Task.Delay().Wait()) instead of Thread.Sleep
        // because it better simulates blocking on external resources like databases or HTTP calls.
        switch (scenario)
        {
            case "SimpleSyncOverAsync":
                FetchDataSync_BLOCKING_HERE(durationSeconds);
                break;
            case "NestedSyncOverAsync":
                ValidateOrderSync_BLOCKS_INTERNALLY(durationSeconds);
                break;
            case "DatabasePattern":
                GetCustomerFromDatabaseSync_SYNC_BLOCK(durationSeconds);
                break;
            default:
                // Default fallback
                Task.Delay(durationSeconds * 1000).Wait();
                break;
        }
        
        var elapsed = DateTimeOffset.UtcNow - startTime;
        
        _logger.LogWarning("🐌 HTTP slow request completed: {Elapsed}s", elapsed.TotalSeconds);
        
        return Ok(new SlowRequestResult
        {
            Message = $"Slow request completed after {elapsed.TotalSeconds:F1} seconds",
            DurationSeconds = elapsed.TotalSeconds,
            Scenario = scenario ?? "Direct",
            StartedAt = startTime,
            CompletedAt = DateTimeOffset.UtcNow
        });
    }

    // =================================================================================
    // SCENARIO 1: Simple Blocking
    // =================================================================================
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FetchDataSync_BLOCKING_HERE(int duration)
    {
        // Simulate blocking call to fetch data
        Task.Delay(duration * 1000).Wait();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProcessDataSync_BLOCKING_HERE() { } 

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SaveDataSync_BLOCKING_HERE() { }


    // =================================================================================
    // SCENARIO 2: Nested Blocking
    // =================================================================================
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ValidateOrderSync_BLOCKS_INTERNALLY(int duration)
    {
        CheckInventorySync_BLOCKS_INTERNALLY(duration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckInventorySync_BLOCKS_INTERNALLY(int duration)
    {
        ProcessPaymentSync_BLOCKS_INTERNALLY(duration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProcessPaymentSync_BLOCKS_INTERNALLY(int duration)
    {
        // Deeply nested blocking call
        Task.Delay(duration * 1000).Wait();
    }


    // =================================================================================
    // SCENARIO 3: Database Pattern
    // =================================================================================
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GetCustomerFromDatabaseSync_SYNC_BLOCK(int duration)
    {
        // Split the duration across multiple "Database" calls
        int partDuration = duration / 3;
        
        // Block 1
        Task.Delay(partDuration * 1000).Wait();
        
        GetOrderHistoryFromDatabaseSync_SYNC_BLOCK(partDuration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GetOrderHistoryFromDatabaseSync_SYNC_BLOCK(int duration)
    {
        // Block 2
        Task.Delay(duration * 1000).Wait();
        
        BuildResponseSync_SYNC_BLOCK(duration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildResponseSync_SYNC_BLOCK(int duration)
    {
        // Block 3
        Task.Delay(duration * 1000).Wait();
    }
}

/// <summary>
/// Result from a slow request execution.
/// </summary>
public class SlowRequestResult
{
    public string Message { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string Scenario { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
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
