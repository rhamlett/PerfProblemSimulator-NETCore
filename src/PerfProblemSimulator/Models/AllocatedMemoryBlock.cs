namespace PerfProblemSimulator.Models;

/// <summary>
/// Represents a block of memory intentionally held by the application.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This class demonstrates how memory can be
/// intentionally "leaked" by holding references to large byte arrays. As long as
/// a reference exists to this block, the garbage collector cannot reclaim it.
/// </para>
/// <para>
/// In real-world memory leak scenarios, similar patterns occur unintentionally:
/// <list type="bullet">
/// <item>Static collections that grow but are never cleared</item>
/// <item>Event handlers that aren't properly unsubscribed</item>
/// <item>Caching without expiration policies</item>
/// <item>Circular references with weak reference misuse</item>
/// </list>
/// </para>
/// </remarks>
public class AllocatedMemoryBlock
{
    /// <summary>
    /// Unique identifier for tracking this block.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Actual size of the allocated memory in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// When this block was allocated.
    /// </summary>
    public DateTimeOffset AllocatedAt { get; init; }

    /// <summary>
    /// The actual allocated memory data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>⚠️ INTENTIONALLY BAD CODE ⚠️</strong>
    /// </para>
    /// <para>
    /// Keeping a reference to this large byte array prevents the garbage collector
    /// from reclaiming it. This is the core mechanism of the memory pressure simulation.
    /// </para>
    /// </remarks>
    public byte[] Data { get; init; } = [];
}
