/*
 * =============================================================================
 * LOAD TEST ENDPOINT - AI PORTABILITY DOCUMENTATION
 * =============================================================================
 * 
 * PURPOSE:
 * This controller provides an endpoint designed to be targeted by Azure Load
 * Testing or similar load testing tools. It simulates realistic application
 * behavior that degrades gracefully under load, eventually leading to timeouts.
 * 
 * ARCHITECTURE OVERVIEW:
 * ┌─────────────────────────────────────────────────────────────────────────┐
 * │  LoadTestController (this file)                                         │
 * │  - HTTP endpoint routing                                                │
 * │  - Request/response handling                                            │
 * │  - Input validation                                                     │
 * └───────────────────────────────┬─────────────────────────────────────────┘
 *                                 │ depends on
 *                                 ▼
 * ┌─────────────────────────────────────────────────────────────────────────┐
 * │  ILoadTestService / LoadTestService                                     │
 * │  - Soft limit tracking (concurrent request counter)                     │
 * │  - Work simulation (CPU + memory)                                       │
 * │  - Degradation delay calculation                                        │
 * │  - Exception throwing after timeout threshold                           │
 * │  File: Services/LoadTestService.cs                                      │
 * └─────────────────────────────────────────────────────────────────────────┘
 * 
 * PORTING TO OTHER LANGUAGES:
 * - PHP: Create a single controller class or route handler
 * - Node.js: Express/Fastify route handler with async middleware
 * - Java: Spring Boot @RestController with @GetMapping
 * - Python: Flask/FastAPI route decorator with async def
 * 
 * DEPENDENCIES (files to also port):
 * 1. Services/ILoadTestService.cs - Interface definition
 * 2. Services/LoadTestService.cs - Core algorithm implementation
 * 3. Models/LoadTestRequest.cs - Request parameter model
 * 4. Models/LoadTestResult.cs - Response model
 * 
 * FRAMEWORK CONCEPTS MAPPING:
 * - [ApiController] → Automatic request binding (like Flask @app.route)
 * - [Route("api/[controller]")] → URL pattern (e.g., /api/loadtest)
 * - ILogger → Logging abstraction (like winston, log4j, monolog)
 * - CancellationToken → Request abort signal (like AbortController in Node)
 * - Task<IActionResult> → Async response (like Promise, CompletableFuture)
 * 
 * =============================================================================
 */

using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for load testing endpoint designed for Azure Load Testing integration.
/// </summary>
/// <remarks>
/// <para>
/// <strong>LOAD TEST ENDPOINT</strong>
/// </para>
/// <para>
/// This endpoint is designed to be targeted by Azure Load Testing or similar tools.
/// Unlike other simulation endpoints, this one does NOT appear in the dashboard UI
/// and is meant for automated load testing scenarios only.
/// </para>
/// <para>
/// <strong>BEHAVIOR UNDER LOAD:</strong>
/// <list type="bullet">
/// <item><term>Low load (below soft limit)</term><description>~100ms response time</description></item>
/// <item><term>Moderate load (at soft limit)</term><description>200-500ms as CPU contention builds</description></item>
/// <item><term>High load (above soft limit)</term><description>Multi-second responses</description></item>
/// <item><term>Extreme load</term><description>Responses approach 230s Azure timeout</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>EXCEPTION BEHAVIOR:</strong>
/// After a request has been processing for 120 seconds, there is a 20% chance per
/// check interval that a random exception will be thrown. This simulates real-world
/// application failures under extreme load.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LoadTestController : ControllerBase
{
    /*
     * =========================================================================
     * DEPENDENCY INJECTION
     * =========================================================================
     * 
     * CONCEPT: Constructor Injection
     * The framework automatically provides instances of these services when
     * creating the controller. This is called "Dependency Injection" (DI).
     * 
     * PORTING NOTES:
     * - PHP (Laravel): Use constructor injection or app()->make()
     * - Node.js: Pass dependencies to factory function or use DI container
     * - Java (Spring): @Autowired or constructor injection
     * - Python (FastAPI): Use Depends() for dependency injection
     * 
     * WHY THIS PATTERN:
     * - Testability: Can inject mock services for unit testing
     * - Loose coupling: Controller doesn't know how to create services
     * - Configuration: Services can be configured at app startup
     */
    private readonly ILoadTestService _loadTestService;
    private readonly ILogger<LoadTestController> _logger;

    /// <summary>
    /// Initializes a new instance of the LoadTestController.
    /// </summary>
    /// <param name="loadTestService">Service that performs the actual load test work.</param>
    /// <param name="logger">Logger for request tracking and diagnostics.</param>
    public LoadTestController(
        ILoadTestService loadTestService,
        ILogger<LoadTestController> logger)
    {
        // Null checks ensure required dependencies are provided
        // PORTING: Most languages have similar null/undefined checks
        _loadTestService = loadTestService ?? throw new ArgumentNullException(nameof(loadTestService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /*
     * =========================================================================
     * ENDPOINT: POST /api/loadtest
     * =========================================================================
     * 
     * HTTP METHOD CHOICE:
     * Using POST because:
     * 1. The operation performs work (not idempotent like GET)
     * 2. Azure Load Testing can easily send POST with JSON body
     * 3. Allows structured parameters in request body
     * 
     * ALTERNATIVE: GET with query parameters
     * GET /api/loadtest?workIterations=1000&bufferSizeKb=100
     * Simpler but less flexible for complex parameters.
     * 
     * URL PATTERN:
     * The [controller] token is replaced with "loadtest" (class name minus "Controller")
     * Full URL: POST https://your-app.azurewebsites.net/api/loadtest
     */

    /// <summary>
    /// Executes a load test probe request that performs lightweight work.
    /// </summary>
    /// <param name="request">Load test parameters as JSON body.</param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request pipeline.</param>
    /// <returns>Load test result with timing and diagnostic information.</returns>
    /// <remarks>
    /// <para>
    /// <strong>ALGORITHM OVERVIEW:</strong>
    /// </para>
    /// <para>
    /// 1. Start a timer (Stopwatch)
    /// 2. Apply baseline blocking delay (guarantees thread pool exhaustion)
    /// 3. Check current concurrent request count
    /// 4. If over soft limit, calculate and apply degradation delay
    /// 5. Perform lightweight CPU work (hash iterations)
    /// 6. Allocate memory buffer (released when request ends)
    /// 7. Periodically check if elapsed time > 120s; if so, 20% chance of exception
    /// 8. Return response with timing details
    /// </para>
    /// <para>
    /// <strong>PARAMETERS:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <term>workIterations (default: 1000)</term>
    /// <description>Number of SHA256 hash computations to perform. Higher = more CPU work.</description>
    /// </item>
    /// <item>
    /// <term>bufferSizeKb (default: 100)</term>
    /// <description>Size of memory buffer to allocate in kilobytes. Released after request.</description>
    /// </item>
    /// <item>
    /// <term>baselineDelayMs (default: 500)</term>
    /// <description>Minimum blocking delay applied to every request. Ensures thread pool exhaustion.</description>
    /// </item>
    /// <item>
    /// <term>softLimit (default: 5)</term>
    /// <description>Concurrent request count before degradation delays begin.</description>
    /// </item>
    /// <item>
    /// <term>degradationFactor (default: 200)</term>
    /// <description>Milliseconds of delay added per concurrent request over the soft limit.</description>
    /// </item>
    /// </list>
    /// <para>
    /// <strong>TOTAL DELAY FORMULA:</strong>
    /// <code>
    /// totalDelay = baselineDelayMs + max(0, currentConcurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// <para>
    /// <strong>EXAMPLE SCENARIOS (with defaults baselineDelayMs=500, softLimit=5, factor=200):</strong>
    /// </para>
    /// <list type="bullet">
    /// <item>1 concurrent request → 500ms baseline only</item>
    /// <item>10 concurrent requests → 500ms + (10-5)×200 = 1500ms total</item>
    /// <item>20 concurrent requests → 500ms + (20-5)×200 = 3500ms total</item>
    /// <item>50 concurrent requests → 500ms + (50-5)×200 = 9500ms total</item>
    /// </list>
    /// </remarks>
    /// <response code="200">Load test completed successfully with timing details.</response>
    /// <response code="500">Request exceeded 120s and random exception was triggered.</response>
    [HttpPost]
    [Consumes("application/json", "text/plain", "application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(LoadTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteLoadTest(
        [FromBody] LoadTestRequest? bodyRequest = null,
        [FromQuery] int? workIterations = null,
        [FromQuery] int? bufferSizeKb = null,
        [FromQuery] int? baselineDelayMs = null,
        [FromQuery] int? softLimit = null,
        [FromQuery] int? degradationFactor = null,
        CancellationToken cancellationToken = default)
    {
        /*
         * =====================================================================
         * FLEXIBLE ENDPOINT - JSON BODY OR QUERY PARAMETERS
         * =====================================================================
         * 
         * Accepts either:
         * 1. POST with JSON body (Content-Type: application/json)
         * 2. GET/POST with query parameters (no Content-Type required)
         * 
         * JSON body takes precedence if provided.
         */
        
        // Use body if provided, otherwise build from query params with defaults
        var request = bodyRequest ?? new LoadTestRequest
        {
            WorkIterations = workIterations ?? 1000,
            BufferSizeKb = bufferSizeKb ?? 100,
            BaselineDelayMs = baselineDelayMs ?? 500,
            SoftLimit = softLimit ?? 5,
            DegradationFactor = degradationFactor ?? 200
        };
        
        _logger.LogDebug(
            "Load test: WorkIterations={WorkIterations}, BufferSizeKb={BufferSizeKb}, BaselineDelayMs={BaselineDelayMs}, SoftLimit={SoftLimit}, DegradationFactor={DegradationFactor}",
            request.WorkIterations,
            request.BufferSizeKb,
            request.BaselineDelayMs,
            request.SoftLimit,
            request.DegradationFactor);

        var result = await _loadTestService.ExecuteWorkAsync(request, cancellationToken);
        return Ok(result);
    }

    /*
     * =========================================================================
     * ENDPOINT: GET /api/loadtest/stats
     * =========================================================================
     * 
     * Returns current load test statistics without performing work.
     * Useful for monitoring concurrent request count during load tests.
     */

    /// <summary>
    /// Gets current load test statistics including concurrent request count.
    /// </summary>
    /// <returns>Current load test statistics.</returns>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(LoadTestStats), StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        /*
         * SYNCHRONOUS ENDPOINT:
         * This returns IActionResult (not Task<IActionResult>) because it does
         * no async work - just reads current statistics.
         * 
         * PORTING:
         * - Node.js: Can be sync or async (Express doesn't care)
         * - Python: def get_stats() vs async def get_stats()
         * - Java: Omit CompletableFuture for sync methods
         * - PHP: No async distinction in traditional PHP
         */
        
        var stats = _loadTestService.GetCurrentStats();
        return Ok(stats);
    }
}

/*
 * =============================================================================
 * RESPONSE MODELS (defined in separate files)
 * =============================================================================
 * 
 * LoadTestResult - File: Models/LoadTestResult.cs
 * Contains: ElapsedMs, ConcurrentRequests, DegradationDelayMs, WorkCompleted, etc.
 * 
 * LoadTestStats - Defined inline in ILoadTestService.cs
 * Contains: CurrentConcurrentRequests, TotalRequestsProcessed, etc.
 * 
 * ErrorResponse - File: Models/ErrorResponse.cs (existing)
 * Contains: Message, Details, TraceId
 * 
 * =============================================================================
 * RELATED FILES TO PORT
 * =============================================================================
 * 
 * 1. Services/ILoadTestService.cs
 *    - Interface defining the service contract
 *    - LoadTestStats record definition
 * 
 * 2. Services/LoadTestService.cs
 *    - Core algorithm implementation
 *    - Concurrent request tracking
 *    - Degradation delay calculation
 *    - Random exception generation
 * 
 * 3. Models/LoadTestRequest.cs
 *    - Request parameter model with defaults
 * 
 * 4. Models/LoadTestResult.cs
 *    - Response model with timing details
 * 
 * =============================================================================
 */
