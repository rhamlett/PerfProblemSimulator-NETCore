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
     * GET /api/loadtest?workIterations=1000&bufferSizeKb=5
     * Simpler but less flexible for complex parameters.
     * 
     * URL PATTERN:
     * The [controller] token is replaced with "loadtest" (class name minus "Controller")
     * Full URL: POST https://your-app.azurewebsites.net/api/loadtest
     */

    /// <summary>
    /// Executes a load test probe request that performs lightweight work.
    /// </summary>
    /// <param name="workIterations">Number of SHA256 hash iterations (default: 1000).</param>
    /// <param name="bufferSizeKb">Memory buffer size in KB (default: 5).</param>
    /// <param name="softLimit">Concurrent request soft limit (default: 50).</param>
    /// <param name="degradationFactor">Delay ms per request over limit (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request pipeline.</param>
    /// <returns>Load test result with timing and diagnostic information.</returns>
    /// <remarks>
    /// <para>
    /// <strong>ALGORITHM OVERVIEW:</strong>
    /// </para>
    /// <para>
    /// 1. Start a timer (Stopwatch)
    /// 2. Check current concurrent request count
    /// 3. If over soft limit, calculate and apply degradation delay
    /// 4. Perform lightweight CPU work (hash iterations)
    /// 5. Allocate small memory buffer (released when request ends)
    /// 6. Periodically check if elapsed time > 120s; if so, 20% chance of exception
    /// 7. Return response with timing details
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
    /// <term>bufferSizeKb (default: 5)</term>
    /// <description>Size of memory buffer to allocate in kilobytes. Released after request.</description>
    /// </item>
    /// <item>
    /// <term>softLimit (default: 50)</term>
    /// <description>Concurrent request count before degradation delays begin.</description>
    /// </item>
    /// <item>
    /// <term>degradationFactor (default: 5)</term>
    /// <description>Milliseconds of delay added per concurrent request over the soft limit.</description>
    /// </item>
    /// </list>
    /// <para>
    /// <strong>DEGRADATION FORMULA:</strong>
    /// <code>
    /// additionalDelayMs = max(0, currentConcurrent - softLimit) * degradationFactor
    /// </code>
    /// </para>
    /// <para>
    /// <strong>EXAMPLE SCENARIOS:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item>10 concurrent requests, softLimit=50 → 0ms added delay</item>
    /// <item>60 concurrent requests, softLimit=50, factor=5 → 50ms added delay</item>
    /// <item>150 concurrent requests, softLimit=50, factor=5 → 500ms added delay</item>
    /// </list>
    /// </remarks>
    /// <response code="200">Load test completed successfully with timing details.</response>
    /// <response code="500">Request exceeded 120s and random exception was triggered.</response>
    [HttpPost]
    [HttpGet]
    [ProducesResponseType(typeof(LoadTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteLoadTest(
        [FromQuery] int workIterations = 1000,
        [FromQuery] int bufferSizeKb = 5,
        [FromQuery] int softLimit = 50,
        [FromQuery] int degradationFactor = 5,
        CancellationToken cancellationToken = default)
    {
        /*
         * =====================================================================
         * REQUEST HANDLING FLOW
         * =====================================================================
         * 
         * This endpoint now accepts query parameters for maximum compatibility
         * with Azure Load Testing, JMeter, and browser testing.
         * 
         * Examples:
         * - GET/POST /api/loadtest (uses all defaults)
         * - GET/POST /api/loadtest?softLimit=25&degradationFactor=10
         * 
         * PORTING NOTES:
         * - Query parameters are universal across HTTP clients
         * - No Content-Type header required
         * - Works with any load testing tool
         */
        
        // Build request from query parameters
        var request = new LoadTestRequest
        {
            WorkIterations = workIterations,
            BufferSizeKb = bufferSizeKb,
            SoftLimit = softLimit,
            DegradationFactor = degradationFactor
        };

        _logger.LogDebug(
            "Load test request received: WorkIterations={WorkIterations}, BufferSizeKb={BufferSizeKb}, SoftLimit={SoftLimit}",
            request.WorkIterations,
            request.BufferSizeKb,
            request.SoftLimit);

        /*
         * =====================================================================
         * SERVICE DELEGATION
         * =====================================================================
         * 
         * WHY SEPARATE SERVICE:
         * - Controller handles HTTP concerns (routing, request/response)
         * - Service handles business logic (the actual algorithm)
         * - This separation makes the logic testable and reusable
         * 
         * ASYNC/AWAIT:
         * The "await" keyword suspends this method until the service completes.
         * This does NOT block a thread - the thread returns to the pool.
         * 
         * PORTING:
         * - Node.js: const result = await loadTestService.executeWork(...)
         * - Python: result = await load_test_service.execute_work(...)
         * - Java: Result result = loadTestService.executeWork(...).get()
         * - PHP: $result = $loadTestService->executeWork(...) (sync or with ReactPHP)
         */
        var result = await _loadTestService.ExecuteWorkAsync(request, cancellationToken);

        /*
         * =====================================================================
         * RESPONSE FORMATTING
         * =====================================================================
         * 
         * Ok(result) returns HTTP 200 with JSON body
         * The framework automatically serializes the result object to JSON
         * 
         * PORTING:
         * - Node.js: res.json(result) or return result (Fastify)
         * - Python: return result (FastAPI auto-serializes)
         * - Java: return ResponseEntity.ok(result)
         * - PHP: return response()->json($result)
         */
        return Ok(result);
    }

    /*
     * =========================================================================
     * ADDITIONAL ENDPOINT: GET /api/loadtest/probe
     * =========================================================================
     * 
     * This simpler endpoint uses query parameters instead of JSON body.
     * Useful for quick testing from a browser or simple HTTP clients.
     */

    /// <summary>
    /// Simplified load test probe using query parameters.
    /// </summary>
    /// <param name="workIterations">Number of hash iterations (default: 1000).</param>
    /// <param name="bufferSizeKb">Memory buffer size in KB (default: 5).</param>
    /// <param name="softLimit">Concurrent request soft limit (default: 50).</param>
    /// <param name="degradationFactor">Delay ms per request over limit (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Load test result with timing details.</returns>
    [HttpGet("probe")]
    [ProducesResponseType(typeof(LoadTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteLoadTestProbe(
        [FromQuery] int workIterations = 1000,
        [FromQuery] int bufferSizeKb = 5,
        [FromQuery] int softLimit = 50,
        [FromQuery] int degradationFactor = 5,
        CancellationToken cancellationToken = default)
    {
        /*
         * QUERY PARAMETER BINDING:
         * [FromQuery] tells the framework to read from URL query string
         * Example: GET /api/loadtest/probe?workIterations=2000&softLimit=25
         * 
         * DEFAULT VALUES:
         * The = 1000, = 5, etc. provide defaults if parameter is omitted
         * 
         * PORTING:
         * - Node.js: req.query.workIterations || 1000
         * - Python: request.args.get('workIterations', 1000, type=int)
         * - Java: @RequestParam(defaultValue = "1000") int workIterations
         * - PHP: $request->query('workIterations', 1000)
         */

        // Delegate to main endpoint - both now use query parameters
        return await ExecuteLoadTest(workIterations, bufferSizeKb, softLimit, degradationFactor, cancellationToken);
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
