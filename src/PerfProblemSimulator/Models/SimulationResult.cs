namespace PerfProblemSimulator.Models;

/// <summary>
/// Represents the result of triggering a performance problem simulation.
/// Returned by all simulation trigger endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This response model provides comprehensive information
/// about the simulation that was started, including the actual parameters used (which may differ
/// from the requested parameters if limits were applied).
/// </para>
/// <para>
/// The <see cref="ActualParameters"/> dictionary is particularly useful for understanding
/// what the system actually did, especially when validation rules cap requested values.
/// </para>
/// </remarks>
public class SimulationResult
{
    /// <summary>
    /// Unique identifier for tracking this simulation instance.
    /// Use this ID to correlate metrics and logs with the specific simulation.
    /// </summary>
    public Guid SimulationId { get; init; }

    /// <summary>
    /// The type of performance problem that was triggered.
    /// </summary>
    public SimulationType Type { get; init; }

    /// <summary>
    /// Current status of the simulation.
    /// </summary>
    /// <remarks>
    /// Possible values:
    /// <list type="bullet">
    /// <item><term>Started</term><description>Simulation is currently running</description></item>
    /// <item><term>Completed</term><description>Simulation finished successfully</description></item>
    /// <item><term>Failed</term><description>Simulation encountered an error</description></item>
    /// <item><term>Cancelled</term><description>Simulation was cancelled before completion</description></item>
    /// </list>
    /// </remarks>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable description of what happened or is happening.
    /// Provides educational context about the simulation.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The actual parameters used for the simulation.
    /// May differ from requested parameters if limits were applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> Always check this dictionary to understand what
    /// the system actually did. If you requested 500 seconds of CPU stress but the maximum
    /// is 300 seconds, this dictionary will show the actual 300-second duration used.
    /// </para>
    /// Common keys by simulation type:
    /// <list type="bullet">
    /// <item><term>Cpu</term><description>DurationSeconds, ProcessorCount</description></item>
    /// <item><term>Memory</term><description>SizeMegabytes, TotalAllocatedMegabytes</description></item>
    /// <item><term>ThreadBlock</term><description>DelayMilliseconds, ConcurrentRequests</description></item>
    /// </list>
    /// </remarks>
    public Dictionary<string, object>? ActualParameters { get; init; }

    /// <summary>
    /// When this simulation started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When this simulation is expected to complete.
    /// Null for simulations with no defined end (e.g., memory allocation holds until released).
    /// </summary>
    public DateTimeOffset? EstimatedEndAt { get; init; }
}
