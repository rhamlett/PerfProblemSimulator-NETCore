using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for the memory pressure service that allocates and releases memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This service demonstrates memory management
/// concepts including:
/// <list type="bullet">
/// <item>Large Object Heap (LOH) behavior for allocations > 85KB</item>
/// <item>Pinned allocations and their impact on GC</item>
/// <item>Memory pressure and its effects on application performance</item>
/// </list>
/// </para>
/// </remarks>
public interface IMemoryPressureService
{
    /// <summary>
    /// Allocates and holds a block of memory to create memory pressure.
    /// </summary>
    /// <param name="sizeMegabytes">
    /// Amount of memory to allocate. Will be capped to the configured maximum.
    /// </param>
    /// <returns>
    /// A result containing the simulation ID and actual allocation details.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Allocations are cumulative - calling this method multiple times will
    /// accumulate allocated memory until <see cref="ReleaseAllMemory"/> is called.
    /// </para>
    /// </remarks>
    SimulationResult AllocateMemory(int sizeMegabytes);

    /// <summary>
    /// Releases all allocated memory blocks.
    /// </summary>
    /// <param name="forceGc">
    /// If true, forces a garbage collection after releasing references.
    /// Note: GC timing is not guaranteed even when forced.
    /// </param>
    /// <returns>
    /// Details about the released memory.
    /// </returns>
    MemoryReleaseResult ReleaseAllMemory(bool forceGc);

    /// <summary>
    /// Gets the current status of memory allocations.
    /// </summary>
    /// <returns>
    /// Current allocation statistics.
    /// </returns>
    MemoryStatus GetMemoryStatus();
}
