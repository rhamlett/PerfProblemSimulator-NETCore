using Microsoft.AspNetCore.Mvc;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Administrative endpoints for managing the simulator.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This controller provides "escape hatch" functionality for resetting the
/// application state. In production applications, similar administrative
/// endpoints should be:
/// </para>
/// <list type="bullet">
/// <item>Protected by authentication/authorization</item>
/// <item>Rate-limited to prevent abuse</item>
/// <item>Audited/logged for compliance</item>
/// <item>Potentially disabled in production via feature flags</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(
        ISimulationTracker simulationTracker,
        IMemoryPressureService memoryPressureService,
        ILogger<AdminController> logger)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resets all active simulations and releases allocated memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This endpoint performs a full reset of the simulator:
    /// </para>
    /// <list type="bullet">
    /// <item>Releases all allocated memory blocks</item>
    /// <item>Forces garbage collection</item>
    /// <item>Clears active simulation tracking (CPU/ThreadBlock simulations will complete naturally)</item>
    /// </list>
    /// <para>
    /// <strong>Note:</strong> CPU stress and thread blocking simulations cannot be instantly
    /// cancelled - they will complete their configured duration. Only memory allocations
    /// are immediately released.
    /// </para>
    /// </remarks>
    /// <returns>Summary of the reset operation.</returns>
    /// <response code="200">Reset completed successfully.</response>
    [HttpPost("reset-all")]
    [ProducesResponseType(typeof(ResetAllResponse), StatusCodes.Status200OK)]
    public IActionResult ResetAll()
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        _logger.LogWarning(
            "⚠️ Reset-all requested from {ClientIP}. Releasing all resources...",
            clientIp);

        // Get state before reset
        var activeSimulations = _simulationTracker.GetActiveSimulations();
        var memoryStatus = _memoryPressureService.GetMemoryStatus();

        // Release memory (this also unregisters from tracker)
        var releaseResult = _memoryPressureService.ReleaseAllMemory(forceGc: true);

        _logger.LogInformation(
            "Reset complete: Released {BlocksReleased} memory blocks ({BytesReleased} bytes), " +
            "had {ActiveSimulations} active simulations",
            releaseResult.ReleasedBlockCount,
            releaseResult.ReleasedBytes,
            activeSimulations.Count);

        return Ok(new ResetAllResponse
        {
            Success = true,
            Message = "Reset completed. Memory released. Note: CPU and ThreadBlock simulations will complete naturally.",
            MemoryBlocksReleased = releaseResult.ReleasedBlockCount,
            BytesReleased = releaseResult.ReleasedBytes,
            ActiveSimulationsAtReset = activeSimulations.Count,
            SimulationTypesActive = activeSimulations
                .GroupBy(s => s.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            GarbageCollectionForced = true
        });
    }

    /// <summary>
    /// Gets current simulation statistics.
    /// </summary>
    /// <returns>Statistics about active and historical simulations.</returns>
    /// <response code="200">Returns simulation statistics.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(SimulationStats), StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var activeSimulations = _simulationTracker.GetActiveSimulations();
        var memoryStatus = _memoryPressureService.GetMemoryStatus();

        ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

        return Ok(new SimulationStats
        {
            ActiveSimulationCount = activeSimulations.Count,
            SimulationsByType = activeSimulations
                .GroupBy(s => s.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            MemoryAllocated = new MemoryStats
            {
                BlockCount = memoryStatus.AllocatedBlocksCount,
                TotalBytes = memoryStatus.TotalAllocatedBytes,
                TotalMegabytes = memoryStatus.TotalAllocatedBytes / (1024.0 * 1024.0)
            },
            ThreadPool = new ThreadPoolStats
            {
                AvailableWorkerThreads = availableWorker,
                MaxWorkerThreads = maxWorker,
                UsedWorkerThreads = maxWorker - availableWorker,
                AvailableIoThreads = availableIo,
                MaxIoThreads = maxIo,
                PendingWorkItems = ThreadPool.PendingWorkItemCount
            },
            ProcessInfo = new ProcessStats
            {
                ProcessorCount = Environment.ProcessorCount,
                WorkingSetBytes = Environment.WorkingSet,
                ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false)
            }
        });
    }
}

/// <summary>
/// Response from the reset-all operation.
/// </summary>
public class ResetAllResponse
{
    /// <summary>
    /// Whether the reset was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message about the reset.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Number of memory blocks that were released.
    /// </summary>
    public int MemoryBlocksReleased { get; init; }

    /// <summary>
    /// Total bytes of memory that were released.
    /// </summary>
    public long BytesReleased { get; init; }

    /// <summary>
    /// Number of active simulations at the time of reset.
    /// </summary>
    public int ActiveSimulationsAtReset { get; init; }

    /// <summary>
    /// Breakdown of active simulations by type.
    /// </summary>
    public Dictionary<string, int> SimulationTypesActive { get; init; } = new();

    /// <summary>
    /// Whether garbage collection was forced.
    /// </summary>
    public bool GarbageCollectionForced { get; init; }
}

/// <summary>
/// Current simulation statistics.
/// </summary>
public class SimulationStats
{
    /// <summary>
    /// Total number of active simulations.
    /// </summary>
    public int ActiveSimulationCount { get; init; }

    /// <summary>
    /// Breakdown of simulations by type.
    /// </summary>
    public Dictionary<string, int> SimulationsByType { get; init; } = new();

    /// <summary>
    /// Memory allocation statistics.
    /// </summary>
    public required MemoryStats MemoryAllocated { get; init; }

    /// <summary>
    /// Thread pool statistics.
    /// </summary>
    public required ThreadPoolStats ThreadPool { get; init; }

    /// <summary>
    /// Process information.
    /// </summary>
    public required ProcessStats ProcessInfo { get; init; }
}

/// <summary>
/// Memory allocation statistics.
/// </summary>
public class MemoryStats
{
    /// <summary>
    /// Number of allocated memory blocks.
    /// </summary>
    public int BlockCount { get; init; }

    /// <summary>
    /// Total allocated bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Total allocated megabytes.
    /// </summary>
    public double TotalMegabytes { get; init; }
}

/// <summary>
/// Thread pool statistics.
/// </summary>
public class ThreadPoolStats
{
    /// <summary>
    /// Available worker threads.
    /// </summary>
    public int AvailableWorkerThreads { get; init; }

    /// <summary>
    /// Maximum worker threads.
    /// </summary>
    public int MaxWorkerThreads { get; init; }

    /// <summary>
    /// Currently used worker threads.
    /// </summary>
    public int UsedWorkerThreads { get; init; }

    /// <summary>
    /// Available I/O completion threads.
    /// </summary>
    public int AvailableIoThreads { get; init; }

    /// <summary>
    /// Maximum I/O completion threads.
    /// </summary>
    public int MaxIoThreads { get; init; }

    /// <summary>
    /// Number of pending work items in the queue.
    /// </summary>
    public long PendingWorkItems { get; init; }
}

/// <summary>
/// Process information statistics.
/// </summary>
public class ProcessStats
{
    /// <summary>
    /// Number of processors available.
    /// </summary>
    public int ProcessorCount { get; init; }

    /// <summary>
    /// Process working set in bytes.
    /// </summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>
    /// Managed heap size in bytes.
    /// </summary>
    public long ManagedHeapBytes { get; init; }
}
