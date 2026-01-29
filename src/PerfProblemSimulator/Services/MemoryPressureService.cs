using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;
using System.Runtime.InteropServices;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that creates memory pressure by allocating and holding large byte arrays.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ EDUCATIONAL PURPOSE ONLY ⚠️</strong>
/// </para>
/// <para>
/// This service intentionally implements memory allocation patterns that would be
/// problematic in production code. It's designed to demonstrate:
/// <list type="bullet">
/// <item>
/// <term>Large Object Heap (LOH) impact</term>
/// <description>
/// Objects larger than 85KB go directly to the LOH, which is collected less frequently
/// and can lead to memory fragmentation. Our allocations (minimum 10MB) are always LOH.
/// </description>
/// </item>
/// <item>
/// <term>Pinned allocations</term>
/// <description>
/// Using <c>GC.AllocateArray(pinned: true)</c> prevents the GC from moving the memory,
/// which can cause heap fragmentation and degrade performance over time.
/// </description>
/// </item>
/// <item>
/// <term>Memory leaks</term>
/// <description>
/// By holding references in a static list, we prevent the GC from reclaiming memory,
/// simulating what happens when applications have actual memory leaks.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <strong>Real-World Memory Leak Causes:</strong>
/// <list type="bullet">
/// <item>Static collections that accumulate data</item>
/// <item>Event handlers not being unsubscribed</item>
/// <item>Improper IDisposable implementation</item>
/// <item>Caching without size limits or expiration</item>
/// <item>Keeping references to large objects longer than needed</item>
/// </list>
/// </para>
/// <para>
/// <strong>Diagnosis Tools:</strong>
/// <list type="bullet">
/// <item>dotnet-dump: <c>dotnet-dump collect -p {PID}</c></item>
/// <item>dotnet-gcdump: <c>dotnet-gcdump collect -p {PID}</c></item>
/// <item>Visual Studio Memory Profiler</item>
/// <item>Application Insights: Memory metrics and profiler</item>
/// <item>Azure App Service: Memory Working Set blade</item>
/// </list>
/// </para>
/// </remarks>
public class MemoryPressureService : IMemoryPressureService
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<MemoryPressureService> _logger;
    private readonly ProblemSimulatorOptions _options;

    /// <summary>
    /// Thread-safe list holding all allocated memory blocks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>⚠️ THIS IS AN ANTI-PATTERN - FOR EDUCATIONAL PURPOSES ONLY ⚠️</strong>
    /// </para>
    /// <para>
    /// This static list holds references to large byte arrays, preventing the garbage
    /// collector from reclaiming them. This simulates a memory leak where references
    /// to large objects are never released.
    /// </para>
    /// </remarks>
    private readonly List<AllocatedMemoryBlock> _allocatedBlocks = [];
    private readonly object _lock = new();

    /// <summary>
    /// Default allocation size in megabytes when not specified or invalid.
    /// </summary>
    private const int DefaultSizeMegabytes = 100;

    /// <summary>
    /// Minimum allocation size in megabytes.
    /// </summary>
    private const int MinimumSizeMegabytes = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPressureService"/> class.
    /// </summary>
    /// <param name="simulationTracker">Service for tracking active simulations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="options">Configuration options containing limits.</param>
    public MemoryPressureService(
        ISimulationTracker simulationTracker,
        ILogger<MemoryPressureService> logger,
        IOptions<ProblemSimulatorOptions> options)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public SimulationResult AllocateMemory(int sizeMegabytes)
    {
        // ==========================================================================
        // STEP 1: Validate and constrain the allocation size
        // ==========================================================================
        var actualSize = sizeMegabytes <= 0
            ? DefaultSizeMegabytes
            : Math.Max(MinimumSizeMegabytes, sizeMegabytes);

        // Check against remaining capacity
        long currentAllocatedBytes;
        lock (_lock)
        {
            currentAllocatedBytes = _allocatedBlocks.Sum(b => b.SizeBytes);
        }

        var maxBytes = (long)_options.MaxMemoryAllocationMb * 1024 * 1024;
        var remainingCapacity = maxBytes - currentAllocatedBytes;
        var requestedBytes = (long)actualSize * 1024 * 1024;

        if (requestedBytes > remainingCapacity)
        {
            // Cap to remaining capacity (minimum 10MB if any space left)
            actualSize = (int)(remainingCapacity / (1024 * 1024));
            if (actualSize < MinimumSizeMegabytes)
            {
                actualSize = 0; // Not enough space for minimum allocation
            }
        }

        if (actualSize <= 0)
        {
            return new SimulationResult
            {
                SimulationId = Guid.Empty,
                Type = SimulationType.Memory,
                Status = "Failed",
                Message = $"Cannot allocate memory: Maximum allocation limit ({_options.MaxMemoryAllocationMb} MB) reached. " +
                          "Release existing allocations first with POST /api/memory/release-memory.",
                ActualParameters = new Dictionary<string, object>
                {
                    ["RequestedSizeMegabytes"] = sizeMegabytes,
                    ["CurrentAllocatedMegabytes"] = currentAllocatedBytes / (1024.0 * 1024.0),
                    ["MaxAllowedMegabytes"] = _options.MaxMemoryAllocationMb
                },
                StartedAt = DateTimeOffset.UtcNow,
                EstimatedEndAt = null
            };
        }

        var simulationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var sizeBytes = (long)actualSize * 1024 * 1024;

        // ==========================================================================
        // STEP 2: Allocate the memory
        // ==========================================================================
        // Using GC.AllocateArray creates a pinned array that the GC cannot move.
        // This is INTENTIONALLY BAD - pinned objects cause heap fragmentation.
        // In production, you should avoid pinning unless absolutely necessary
        // (e.g., for interop with native code).

        byte[] data;
        try
        {
            // Allocate pinned array - this is intentionally inefficient
            // The pinned flag tells the GC not to move this memory, which
            // can cause fragmentation in the managed heap over time.
            data = GC.AllocateArray<byte>((int)sizeBytes, pinned: true);

            // Touch the memory to ensure it's actually committed
            // Without this, the OS might not actually allocate physical pages
            Array.Fill(data, (byte)0xAB);
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "Out of memory allocating {Size} MB", actualSize);
            return new SimulationResult
            {
                SimulationId = Guid.Empty,
                Type = SimulationType.Memory,
                Status = "Failed",
                Message = $"Out of memory attempting to allocate {actualSize} MB. Try a smaller allocation.",
                ActualParameters = new Dictionary<string, object>
                {
                    ["RequestedSizeMegabytes"] = actualSize
                },
                StartedAt = startedAt,
                EstimatedEndAt = null
            };
        }

        // ==========================================================================
        // STEP 3: Store the reference to prevent garbage collection
        // ==========================================================================
        var block = new AllocatedMemoryBlock
        {
            Id = simulationId,
            SizeBytes = sizeBytes,
            AllocatedAt = startedAt,
            Data = data
        };

        lock (_lock)
        {
            _allocatedBlocks.Add(block);
        }

        var parameters = new Dictionary<string, object>
        {
            ["SizeMegabytes"] = actualSize,
            ["SizeBytes"] = sizeBytes,
            ["TotalAllocatedMegabytes"] = GetTotalAllocatedMegabytes()
        };

        // Register with simulation tracker
        var cts = new CancellationTokenSource(); // Memory allocations don't timeout
        _simulationTracker.RegisterSimulation(simulationId, SimulationType.Memory, parameters, cts);

        _logger.LogInformation(
            "Allocated {Size} MB (block {BlockId}). Total allocated: {Total} MB",
            actualSize,
            simulationId,
            GetTotalAllocatedMegabytes());

        return new SimulationResult
        {
            SimulationId = simulationId,
            Type = SimulationType.Memory,
            Status = "Started",
            Message = $"Allocated {actualSize} MB of memory. Total allocated: {GetTotalAllocatedMegabytes():F1} MB. " +
                      "This memory is pinned to the Large Object Heap and will not be garbage collected until released. " +
                      "Observe the Working Set metric in Task Manager or dotnet-counters. " +
                      "In real applications, memory leaks like this are often caused by static collections, unclosed streams, or event handler accumulation.",
            ActualParameters = parameters,
            StartedAt = startedAt,
            EstimatedEndAt = null // Memory stays until explicitly released
        };
    }

    /// <inheritdoc />
    public MemoryReleaseResult ReleaseAllMemory(bool forceGc)
    {
        int releasedCount;
        long releasedBytes;

        lock (_lock)
        {
            releasedCount = _allocatedBlocks.Count;
            releasedBytes = _allocatedBlocks.Sum(b => b.SizeBytes);

            // Unregister all simulations
            foreach (var block in _allocatedBlocks)
            {
                _simulationTracker.UnregisterSimulation(block.Id);
            }

            // Clear the list - this removes all references, making the
            // byte arrays eligible for garbage collection
            _allocatedBlocks.Clear();
        }

        _logger.LogInformation(
            "Released {Count} memory blocks ({Size} MB). ForceGC: {ForceGC}",
            releasedCount,
            releasedBytes / (1024.0 * 1024.0),
            forceGc);

        // ==========================================================================
        // Optional: Force garbage collection
        // ==========================================================================
        // Calling GC.Collect() is generally discouraged in production code because:
        // 1. The GC is highly optimized and usually knows best when to collect
        // 2. Forcing collection causes all threads to pause (GC pause)
        // 3. It doesn't guarantee immediate memory return to the OS
        //
        // However, for educational purposes, it helps demonstrate the difference
        // between releasing references (eligible for GC) and actual memory reclamation.

        if (forceGc)
        {
            _logger.LogInformation("Forcing garbage collection...");

            // Collect all generations
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            _logger.LogInformation("Garbage collection completed");
        }

        return new MemoryReleaseResult
        {
            ReleasedBlockCount = releasedCount,
            ReleasedBytes = releasedBytes,
            ForcedGarbageCollection = forceGc,
            Message = releasedCount > 0
                ? $"Released {releasedCount} memory blocks ({releasedBytes / (1024.0 * 1024.0):F1} MB). " +
                  (forceGc
                      ? "Forced GC to reclaim memory. Working Set should decrease shortly."
                      : "Memory is now eligible for garbage collection but timing is non-deterministic.")
                : "No memory blocks were allocated."
        };
    }

    /// <inheritdoc />
    public MemoryStatus GetMemoryStatus()
    {
        lock (_lock)
        {
            return new MemoryStatus
            {
                AllocatedBlocksCount = _allocatedBlocks.Count,
                TotalAllocatedBytes = _allocatedBlocks.Sum(b => b.SizeBytes),
                OldestAllocationAt = _allocatedBlocks.Count > 0
                    ? _allocatedBlocks.Min(b => b.AllocatedAt)
                    : null,
                NewestAllocationAt = _allocatedBlocks.Count > 0
                    ? _allocatedBlocks.Max(b => b.AllocatedAt)
                    : null
            };
        }
    }

    private double GetTotalAllocatedMegabytes()
    {
        lock (_lock)
        {
            return _allocatedBlocks.Sum(b => b.SizeBytes) / (1024.0 * 1024.0);
        }
    }
}
