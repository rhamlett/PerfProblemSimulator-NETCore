namespace PerfProblemSimulator.Models;

/// <summary>
/// Result of releasing allocated memory blocks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Response model for the memory release endpoint. Confirms how much memory was freed
/// and whether garbage collection was triggered. This helps users understand memory
/// management behavior when releasing pressure.
/// </para>
/// <para>
/// <strong>WHY TRACK RELEASED BLOCKS:</strong>
/// <list type="bullet">
/// <item>Confirms the release actually happened (blocks weren't already freed)</item>
/// <item>Shows bytes freed so user can verify expected amount</item>
/// <item>Indicates whether GC was forced (affects timing of actual memory return to OS)</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Memory is automatically freed when variables go out of scope</item>
/// <item>Node.js: Set arrays to null and optionally call global.gc() if --expose-gc flag set</item>
/// <item>Java: Clear references and call System.gc() (advisory)</item>
/// <item>Python: del references and call gc.collect()</item>
/// </list>
/// </para>
/// </remarks>
public class MemoryReleaseResult
{
    /// <summary>
    /// Number of memory blocks that were released.
    /// </summary>
    public int ReleasedBlockCount { get; init; }

    /// <summary>
    /// Total memory released in bytes.
    /// </summary>
    public long ReleasedBytes { get; init; }

    /// <summary>
    /// Total memory released in megabytes.
    /// </summary>
    public double ReleasedMegabytes => ReleasedBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Whether garbage collection was forced after release.
    /// </summary>
    public bool ForcedGarbageCollection { get; init; }

    /// <summary>
    /// Human-readable message about the release operation.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Current status of memory allocations.
/// </summary>
public class MemoryStatus
{
    /// <summary>
    /// Number of currently allocated memory blocks.
    /// </summary>
    public int AllocatedBlocksCount { get; init; }

    /// <summary>
    /// Total size of all allocated blocks in bytes.
    /// </summary>
    public long TotalAllocatedBytes { get; init; }

    /// <summary>
    /// Total size of all allocated blocks in megabytes.
    /// </summary>
    public double TotalAllocatedMegabytes => TotalAllocatedBytes / (1024.0 * 1024.0);

    /// <summary>
    /// When the oldest block was allocated (null if no blocks).
    /// </summary>
    public DateTimeOffset? OldestAllocationAt { get; init; }

    /// <summary>
    /// When the newest block was allocated (null if no blocks).
    /// </summary>
    public DateTimeOffset? NewestAllocationAt { get; init; }
}
