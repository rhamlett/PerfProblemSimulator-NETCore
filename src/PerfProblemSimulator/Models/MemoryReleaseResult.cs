namespace PerfProblemSimulator.Models;

/// <summary>
/// Result of releasing allocated memory blocks.
/// </summary>
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
