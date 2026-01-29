using System.ComponentModel.DataAnnotations;

namespace PerfProblemSimulator.Models;

/// <summary>
/// Request model for triggering high CPU usage simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This request model demonstrates the use of
/// data annotations for input validation. ASP.NET Core automatically validates
/// incoming requests against these attributes and returns 400 Bad Request with
/// details if validation fails.
/// </para>
/// <para>
/// The <see cref="DurationSeconds"/> property has a default value of 30 seconds,
/// which provides enough time to observe the CPU spike without leaving it running
/// indefinitely.
/// </para>
/// </remarks>
public class CpuStressRequest
{
    /// <summary>
    /// How long the CPU stress should run in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: 30 seconds. This is enough time to observe CPU metrics spike
    /// in monitoring tools like Task Manager, dotnet-counters, or Application Insights.
    /// </para>
    /// <para>
    /// Values exceeding the configured maximum (default: 300 seconds) will be
    /// automatically capped to prevent runaway CPU consumption.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "Duration must be at least 1 second")]
    public int DurationSeconds { get; set; } = 30;
}
