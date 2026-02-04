using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for the CPU stress service that triggers high CPU usage simulations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> Using an interface allows for:
/// <list type="bullet">
/// <item>Dependency injection and loose coupling</item>
/// <item>Easy mocking in unit tests</item>
/// <item>Potential for different implementations (e.g., a safer "fake" stress for testing)</item>
/// </list>
/// </para>
/// </remarks>
public interface ICpuStressService
{
    /// <summary>
    /// Triggers a CPU stress simulation that runs for the specified duration.
    /// </summary>
    /// <param name="durationSeconds">
    /// How long to sustain high CPU usage. Will be capped to the configured maximum.
    /// </param>
    /// <param name="cancellationToken">
    /// Token to request early cancellation of the stress operation.
    /// </param>
    /// <param name="targetPercentage">
    /// Target CPU usage percentage (1-100). Default is 100.
    /// </param>
    /// <returns>
    /// A result containing the simulation ID, actual parameters used, and timing information.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> This method starts the CPU stress asynchronously
    /// and returns immediately with simulation metadata. The actual CPU stress runs in the
    /// background. Use the <see cref="ISimulationTracker"/> to monitor progress or cancel.
    /// </para>
    /// <para>
    /// The returned <see cref="SimulationResult.ActualParameters"/> dictionary will contain:
    /// <list type="bullet">
    /// <item><term>DurationSeconds</term><description>Actual duration used (may be capped)</description></item>
    /// <item><term>ProcessorCount</term><description>Number of cores being stressed</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    Task<SimulationResult> TriggerCpuStressAsync(int durationSeconds, CancellationToken cancellationToken, int targetPercentage = 100);
}
