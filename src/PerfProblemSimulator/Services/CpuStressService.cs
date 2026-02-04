using PerfProblemSimulator.Models;
using System.Diagnostics;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that creates high CPU usage through parallel spin loops.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ EDUCATIONAL PURPOSE ONLY ⚠️</strong>
/// </para>
/// <para>
/// This service intentionally implements an anti-pattern to demonstrate what high CPU usage
/// looks like and how to diagnose it. In production code, you would NEVER do this.
/// </para>
/// <para>
/// <strong>Why This Is Bad:</strong>
/// <list type="bullet">
/// <item>
/// <term>Spin loops waste resources</term>
/// <description>
/// The CPU is doing nothing useful - just incrementing counters and checking conditions.
/// This prevents other threads and processes from using those CPU cycles.
/// </description>
/// </item>
/// <item>
/// <term>Multi-core saturation</term>
/// <description>
/// Using <c>Parallel.For</c> with <c>Environment.ProcessorCount</c> iterations ensures
/// all CPU cores are consumed, making the entire system sluggish.
/// </description>
/// </item>
/// <item>
/// <term>No useful work</term>
/// <description>
/// Unlike legitimate CPU-intensive operations (compression, encryption, calculations),
/// this spin loop produces no output. It's pure waste.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <strong>Real-World Causes of High CPU:</strong>
/// <list type="bullet">
/// <item>Inefficient algorithms (O(n²) when O(n) is possible)</item>
/// <item>Infinite loops due to bugs</item>
/// <item>Excessive regular expression backtracking</item>
/// <item>Unoptimized LINQ queries with large datasets</item>
/// <item>Busy-waiting instead of using async/await or events</item>
/// </list>
/// </para>
/// <para>
/// <strong>Diagnosis Tools:</strong>
/// <list type="bullet">
/// <item>dotnet-counters: <c>dotnet-counters monitor -p {PID} --counters System.Runtime</c></item>
/// <item>dotnet-trace: <c>dotnet-trace collect -p {PID}</c></item>
/// <item>Application Insights: CPU metrics and profiler</item>
/// <item>Azure App Service Diagnostics: CPU usage blade</item>
/// </list>
/// </para>
/// </remarks>
public class CpuStressService : ICpuStressService
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<CpuStressService> _logger;

    /// <summary>
    /// Default duration in seconds when not specified or invalid.
    /// </summary>
    private const int DefaultDurationSeconds = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuStressService"/> class.
    /// </summary>
    /// <param name="simulationTracker">Service for tracking active simulations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public CpuStressService(
        ISimulationTracker simulationTracker,
        ILogger<CpuStressService> logger)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<SimulationResult> TriggerCpuStressAsync(int durationSeconds, CancellationToken cancellationToken, int targetPercentage = 100)
    {
        // ==========================================================================
        // STEP 1: Validate the duration (no upper limits - app is meant to break)
        // ==========================================================================
        var actualDuration = durationSeconds <= 0
            ? DefaultDurationSeconds
            : durationSeconds;

        var actualPercentage = Math.Clamp(targetPercentage, 1, 100);

        var simulationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var estimatedEndAt = startedAt.AddSeconds(actualDuration);
        var processorCount = Environment.ProcessorCount;

        // ==========================================================================
        // STEP 2: Create a linked cancellation token
        // ==========================================================================
        // We combine the caller's cancellation token with our own, so we can cancel
        // from either the external request or internal timeout.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var parameters = new Dictionary<string, object>
        {
            ["DurationSeconds"] = actualDuration,
            ["ProcessorCount"] = processorCount,
            ["TargetPercentage"] = actualPercentage
        };

        // Register this simulation with the tracker
        _simulationTracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters, cts);

        _logger.LogInformation(
            "Starting CPU stress simulation {SimulationId}: {Duration}s @ {Percentage}% across {ProcessorCount} cores",
            simulationId,
            actualDuration,
            actualPercentage,
            processorCount);

        // ==========================================================================
        // STEP 3: Start the CPU stress in the background
        // ==========================================================================
        // We use Task.Run to offload the CPU-intensive work to the thread pool,
        // allowing this method to return immediately with the simulation metadata.
        // This is important because the caller (HTTP request) shouldn't be blocked
        // waiting for the entire duration.

        _ = Task.Run(() => ExecuteCpuStress(simulationId, actualDuration, actualPercentage, cts.Token), cts.Token);

        // ==========================================================================
        // STEP 4: Return the result immediately
        // ==========================================================================
        // The caller gets back the simulation ID and can use it to track progress
        // or cancel the simulation early.

        var result = new SimulationResult
        {
            SimulationId = simulationId,
            Type = SimulationType.Cpu,
            Status = "Started",
            Message = $"CPU stress started on {processorCount} cores for {actualDuration} seconds at {actualPercentage}%. " +
                      "Observe CPU metrics in Task Manager, dotnet-counters, or Application Insights. " +
                      "High CPU like this is typically caused by spin loops, inefficient algorithms, or infinite loops.",
            ActualParameters = parameters,
            StartedAt = startedAt,
            EstimatedEndAt = estimatedEndAt
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Executes the actual CPU stress operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>⚠️ THIS IS AN ANTI-PATTERN - FOR EDUCATIONAL PURPOSES ONLY ⚠️</strong>
    /// </para>
    /// <para>
    /// This method uses dedicated threads with spin loops to consume available CPU.
    /// If targetPercentage is 100, it runs a tight loop.
    /// If targetPercentage is less than 100, it uses a duty cycle (work/sleep) to simulate load.
    /// </para>
    /// <para>
    /// <strong>Why Dedicated Threads Instead of Parallel.For?</strong>
    /// Using <c>Parallel.For</c> would consume thread pool threads, which can interfere with
    /// ASP.NET Core request handling and SignalR. By using dedicated threads, we ensure the
    /// thread pool remains available for the dashboard and metrics collection.
    /// </para>
    /// </remarks>
    private void ExecuteCpuStress(Guid simulationId, int durationSeconds, int targetPercentage, CancellationToken cancellationToken)
    {
        try
        {
            // Calculate the end time using Stopwatch for high precision
            var endTime = Stopwatch.GetTimestamp() + (durationSeconds * Stopwatch.Frequency);
            var processorCount = Environment.ProcessorCount;

            // ==========================================================================
            // THE ANTI-PATTERN: Dedicated thread spin loops
            // ==========================================================================
            // This code intentionally creates one dedicated thread per CPU core.
            
            var threads = new Thread[processorCount];
            
            for (int i = 0; i < processorCount; i++)
            {
                var threadIndex = i;
                threads[i] = new Thread(() =>
                {
                    if (targetPercentage >= 99)
                    {
                        // 100% Load: Tight spin loop (simplest, most effective for maxing out core)
                        while (Stopwatch.GetTimestamp() < endTime && !cancellationToken.IsCancellationRequested)
                        {
                            // Intentionally empty - this is a spin loop
                        }
                    }
                    else
                    {
                        // Partial Load: Duty Cycle
                        // Work for X ms, Sleep for Y ms
                        // Using a small window (e.g., 200ms) keeps usage relatively smooth
                        // while being large enough to reduce the impact of Thread.Sleep inaccuracy.
                        const int windowMs = 200;
                        int workMs = (windowMs * targetPercentage) / 100;
                        int sleepMs = windowMs - workMs;

                        // Stagger start times to desynchronize the duty cycles across cores.
                        // This prevents "spiky" aggregate CPU usage where all cores sleep simultaneously.
                        // We distribute the start times evenly across the window.
                        if (processorCount > 1)
                        {
                            int startDelay = (windowMs * threadIndex) / processorCount;
                            Thread.Sleep(startDelay);
                        }

                        while (Stopwatch.GetTimestamp() < endTime && !cancellationToken.IsCancellationRequested)
                        {
                            var cycleStart = Stopwatch.GetTimestamp();
                            var workTicks = (workMs * Stopwatch.Frequency) / 1000;
                            
                            // Spin for work portion
                            while (Stopwatch.GetTimestamp() - cycleStart < workTicks)
                            {
                                // Spin
                            }

                            // Sleep for remainder of window
                            // Only sleep if the duration is significant enough (> 20ms) to ensure
                            // we don't undershoot due to timer resolution (15.6ms usually).
                            if (sleepMs > 20)
                            {
                                Thread.Sleep(sleepMs);
                            }
                        }
                    }
                })
                {
                    Name = $"CpuStress-{simulationId:N}-{threadIndex}",
                    IsBackground = true,
                    Priority = ThreadPriority.Normal
                };
            }

            // Start all threads
            foreach (var thread in threads)
            {
                thread.Start();
            }

            // Wait for all threads to complete
            foreach (var thread in threads)
            {
                thread.Join();
            }

            _logger.LogInformation(
                "CPU stress simulation {SimulationId} completed normally after {Duration}s",
                simulationId,
                durationSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "CPU stress simulation {SimulationId} was cancelled",
                simulationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CPU stress simulation {SimulationId} failed with error",
                simulationId);
        }
        finally
        {
            // Always unregister the simulation when done
            _simulationTracker.UnregisterSimulation(simulationId);
        }
    }
}
