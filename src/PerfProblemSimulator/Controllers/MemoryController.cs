using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Controller for memory pressure simulation endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ WARNING: This controller allocates and holds memory intentionally ⚠️</strong>
/// </para>
/// <para>
/// This controller provides endpoints to allocate and release memory for educational
/// and diagnostic purposes. It should only be used in controlled environments for:
/// <list type="bullet">
/// <item>Learning how to diagnose memory issues</item>
/// <item>Testing memory alerts and auto-scaling</item>
/// <item>Understanding garbage collection behavior</item>
/// <item>Practicing with memory profiling tools</item>
/// </list>
/// </para>
/// <para>
/// <strong>Key Concepts Demonstrated:</strong>
/// <list type="bullet">
/// <item><term>Large Object Heap (LOH)</term><description>Objects > 85KB go to LOH</description></item>
/// <item><term>Pinned objects</term><description>GC cannot move pinned memory</description></item>
/// <item><term>Working Set vs Private Bytes</term><description>Different memory metrics</description></item>
/// <item><term>GC generations</term><description>Gen0, Gen1, Gen2 collection patterns</description></item>
/// </list>
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MemoryController : ControllerBase
{
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly ILogger<MemoryController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryController"/> class.
    /// </summary>
    /// <param name="memoryPressureService">Service for memory allocation/release.</param>
    /// <param name="logger">Logger for request tracking.</param>
    public MemoryController(
        IMemoryPressureService memoryPressureService,
        ILogger<MemoryController> logger)
    {
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Allocates and holds a block of memory to create memory pressure.
    /// </summary>
    /// <param name="request">
    /// Optional request body specifying the size. If not provided, defaults are used.
    /// </param>
    /// <returns>
    /// Information about the allocation, including actual size and total allocated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>What This Does:</strong>
    /// </para>
    /// <para>
    /// This endpoint allocates a large byte array and holds a reference to it,
    /// preventing the garbage collector from reclaiming it. The memory is pinned
    /// on the Large Object Heap (LOH), which can cause heap fragmentation.
    /// </para>
    /// <para>
    /// Allocations are cumulative - calling this endpoint multiple times will
    /// accumulate memory until the configured limit is reached.
    /// </para>
    /// <para>
    /// <strong>How to Observe:</strong>
    /// <list type="bullet">
    /// <item><term>Task Manager</term><description>Watch Working Set increase</description></item>
    /// <item><term>dotnet-counters</term><description><c>dotnet-counters monitor -p {PID}</c></description></item>
    /// <item><term>Azure Portal</term><description>App Service → Metrics → Memory Working Set</description></item>
    /// <item><term>Application Insights</term><description>Performance → Memory usage</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <response code="200">Memory allocated successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="403">Problem endpoints are disabled via environment variable.</response>
    [HttpPost("allocate-memory")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public IActionResult AllocateMemory([FromBody] MemoryAllocationRequest? request)
    {
        var sizeMegabytes = request?.SizeMegabytes ?? 100;

        _logger.LogInformation(
            "Received memory allocation request: SizeMegabytes={Size}, ClientIP={ClientIP}",
            sizeMegabytes,
            HttpContext.Connection.RemoteIpAddress);

        try
        {
            var result = _memoryPressureService.AllocateMemory(sizeMegabytes);

            _logger.LogInformation(
                "Memory allocation result: SimulationId={SimulationId}, Status={Status}",
                result.SimulationId,
                result.Status);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to allocate memory");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ErrorResponse.SimulationError("Failed to allocate memory. See server logs for details."));
        }
    }

    /// <summary>
    /// Releases all allocated memory blocks.
    /// </summary>
    /// <param name="request">
    /// Optional request body specifying whether to force garbage collection.
    /// </param>
    /// <returns>
    /// Details about the released memory.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>What This Does:</strong>
    /// </para>
    /// <para>
    /// This endpoint releases all references to allocated memory blocks, making them
    /// eligible for garbage collection. Optionally, it can force an immediate GC.
    /// </para>
    /// <para>
    /// <strong>Important Note:</strong> Even after forcing GC, the Working Set may not
    /// immediately decrease because:
    /// <list type="bullet">
    /// <item>The runtime may keep memory committed for future allocations</item>
    /// <item>The OS manages when physical pages are actually released</item>
    /// <item>Memory pressure thresholds affect when memory is returned</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <response code="200">Memory released successfully.</response>
    /// <response code="403">Problem endpoints are disabled via environment variable.</response>
    [HttpPost("release-memory")]
    [ProducesResponseType(typeof(MemoryReleaseResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public IActionResult ReleaseMemory([FromBody] ReleaseMemoryRequest? request)
    {
        var forceGc = request?.ForceGarbageCollection ?? true;

        _logger.LogInformation(
            "Received memory release request: ForceGC={ForceGC}, ClientIP={ClientIP}",
            forceGc,
            HttpContext.Connection.RemoteIpAddress);

        try
        {
            var result = _memoryPressureService.ReleaseAllMemory(forceGc);

            _logger.LogInformation(
                "Memory release result: ReleasedBlocks={Count}, ReleasedMB={Size}",
                result.ReleasedBlockCount,
                result.ReleasedMegabytes);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release memory");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ErrorResponse.SimulationError("Failed to release memory. See server logs for details."));
        }
    }

    /// <summary>
    /// Gets the current memory allocation status.
    /// </summary>
    /// <returns>
    /// Current memory allocation statistics.
    /// </returns>
    /// <response code="200">Returns memory status.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(MemoryStatus), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var status = _memoryPressureService.GetMemoryStatus();
        return Ok(status);
    }
}

/// <summary>
/// Request model for releasing allocated memory.
/// </summary>
public class ReleaseMemoryRequest
{
    /// <summary>
    /// Whether to force garbage collection after releasing references.
    /// </summary>
    /// <remarks>
    /// Default: true. Forcing GC helps demonstrate immediate memory reclamation,
    /// but in production, you generally shouldn't force GC.
    /// </remarks>
    public bool ForceGarbageCollection { get; set; } = true;
}
