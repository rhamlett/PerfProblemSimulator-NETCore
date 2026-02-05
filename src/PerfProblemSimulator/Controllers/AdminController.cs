using Microsoft.AspNetCore.Http.Timeouts;
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
[RequestTimeout("NoTimeout")] // Admin endpoints must always respond
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
                ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                AzureSku = Environment.GetEnvironmentVariable("WEBSITE_SKU") ?? "Local",
                ComputeMode = Environment.GetEnvironmentVariable("WEBSITE_COMPUTE_MODE")
            }
        });
    }
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

        /// <summary>
        /// The Azure SKU (Pricing Tier) if running in Azure App Service (e.g., P0V3, Standard, Basic).
        /// </summary>
        public string? AzureSku { get; init; }

        /// <summary>
        /// The compute mode (e.g., Dedicated, Shared).
        /// </summary>
        public string? ComputeMode { get; init; }
    }
