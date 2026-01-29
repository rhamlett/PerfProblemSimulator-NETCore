using System.ComponentModel.DataAnnotations;

namespace PerfProblemSimulator.Models;

/// <summary>
/// Request model for allocating memory to create memory pressure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> Memory pressure occurs when an application
/// consumes a significant portion of available memory, forcing the garbage collector
/// to work harder and potentially causing performance degradation or out-of-memory errors.
/// </para>
/// <para>
/// The default size of 100 MB is large enough to be observable in memory metrics
/// but small enough to be safely allocated multiple times for testing.
/// </para>
/// </remarks>
public class MemoryAllocationRequest
{
    /// <summary>
    /// Amount of memory to allocate in megabytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: 100 MB. This provides a noticeable increase in working set
    /// without immediately consuming all available memory.
    /// </para>
    /// <para>
    /// Allocations are additive - calling this endpoint multiple times will
    /// accumulate allocated memory until explicitly released.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "Size must be at least 1 MB")]
    public int SizeMegabytes { get; set; } = 100;
}
