using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that causes thread pool starvation through sync-over-async blocking.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️⚠️⚠️ EDUCATIONAL PURPOSE ONLY - NEVER DO THIS IN PRODUCTION ⚠️⚠️⚠️</strong>
/// </para>
/// <para>
/// This service demonstrates the sync-over-async anti-pattern, which is one of the
/// most dangerous and common performance problems in ASP.NET applications. The code
/// in this service is INTENTIONALLY BAD to show what NOT to do.
/// </para>
/// <para>
/// <strong>What is Sync-Over-Async?</strong>
/// </para>
/// <para>
/// Sync-over-async occurs when synchronous code blocks waiting for an asynchronous
/// operation to complete. Common patterns include:
/// <code>
/// // ❌ BAD - Blocks thread waiting for Task
/// var result = SomeAsyncMethod().Result;
/// var result = SomeAsyncMethod().GetAwaiter().GetResult();
/// SomeAsyncMethod().Wait();
/// 
/// // ✅ GOOD - Properly awaits the Task
/// var result = await SomeAsyncMethod();
/// </code>
/// </para>
/// <para>
/// <strong>Why is this Bad?</strong>
/// </para>
/// <para>
/// When you call <c>.Result</c> or <c>.Wait()</c> on a Task, the current thread is
/// blocked (suspended) while waiting for the async operation. In ASP.NET:
/// <list type="number">
/// <item>Request comes in, gets a thread from the thread pool</item>
/// <item>Code hits <c>.Result</c> on an async operation</item>
/// <item>Thread is now BLOCKED, doing nothing, just waiting</item>
/// <item>If many requests do this, all threads become blocked</item>
/// <item>New requests have no threads available = STARVATION</item>
/// <item>Application appears hung even though it's just waiting for threads</item>
/// </list>
/// </para>
/// <para>
/// <strong>The Deadlock Risk:</strong>
/// </para>
/// <para>
/// In ASP.NET (non-Core) and some UI frameworks, sync-over-async can cause DEADLOCKS
/// because the async continuation needs to run on the same thread that's blocked waiting.
/// ASP.NET Core's thread pool doesn't have a synchronization context, so deadlocks are
/// less common, but thread starvation still occurs.
/// </para>
/// <para>
/// <strong>Real-World Causes:</strong>
/// <list type="bullet">
/// <item>Calling async libraries from sync code: <c>httpClient.GetAsync(url).Result</c></item>
/// <item>Mixing sync and async in constructors</item>
/// <item>Using <c>.Result</c> in properties</item>
/// <item>Calling async methods from Dispose()</item>
/// <item>Third-party libraries that block internally</item>
/// </list>
/// </para>
/// <para>
/// <strong>How to Diagnose:</strong>
/// <list type="bullet">
/// <item>dotnet-counters: Watch "ThreadPool Thread Count" and "ThreadPool Queue Length"</item>
/// <item>Response times suddenly spike for ALL endpoints</item>
/// <item>CPU is LOW but requests are slow (threads are waiting, not working)</item>
/// <item>Application Insights shows high request queuing time</item>
/// </list>
/// </para>
/// <para>
/// <strong>How to Fix:</strong>
/// <list type="bullet">
/// <item>Use <c>await</c> all the way up the call stack (async all the way down)</item>
/// <item>If you absolutely must call async from sync, use <c>Task.Run(() => ...).Result</c>
/// to at least not block the request thread (but this is still not ideal)</item>
/// <item>Consider making your sync callers async</item>
/// </list>
/// </para>
/// </remarks>
public class ThreadBlockService : IThreadBlockService
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<ThreadBlockService> _logger;
    private readonly ProblemSimulatorOptions _options;

    /// <summary>
    /// Default delay in milliseconds when not specified or invalid.
    /// </summary>
    private const int DefaultDelayMs = 1000;

    /// <summary>
    /// Default number of concurrent blocking requests.
    /// </summary>
    private const int DefaultConcurrentRequests = 10;

    /// <summary>
    /// Minimum delay in milliseconds.
    /// </summary>
    private const int MinimumDelayMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadBlockService"/> class.
    /// </summary>
    public ThreadBlockService(
        ISimulationTracker simulationTracker,
        ILogger<ThreadBlockService> logger,
        IOptions<ProblemSimulatorOptions> options)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<SimulationResult> TriggerSyncOverAsyncAsync(
        int delayMilliseconds,
        int concurrentRequests,
        CancellationToken cancellationToken)
    {
        // ==========================================================================
        // STEP 1: Validate and constrain parameters
        // ==========================================================================
        var actualDelay = delayMilliseconds <= 0
            ? DefaultDelayMs
            : Math.Max(MinimumDelayMs, Math.Min(delayMilliseconds, _options.MaxThreadBlockDelayMs));

        var actualConcurrent = concurrentRequests <= 0
            ? DefaultConcurrentRequests
            : Math.Min(concurrentRequests, _options.MaxConcurrentBlockingRequests);

        var simulationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        // Estimate end time based on delay (all concurrent requests start together)
        var estimatedEndAt = startedAt.AddMilliseconds(actualDelay);

        // Create linked cancellation token
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Get current thread pool stats for logging
        ThreadPool.GetAvailableThreads(out var workerThreads, out var ioThreads);
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

        var parameters = new Dictionary<string, object>
        {
            ["DelayMilliseconds"] = actualDelay,
            ["ConcurrentRequests"] = actualConcurrent,
            ["ThreadPoolAvailableWorkers"] = workerThreads,
            ["ThreadPoolMinWorkers"] = minWorker,
            ["ThreadPoolMaxWorkers"] = maxWorker
        };

        // Register simulation
        _simulationTracker.RegisterSimulation(simulationId, SimulationType.ThreadBlock, parameters, cts);

        _logger.LogWarning(
            "⚠️ Starting sync-over-async simulation {SimulationId}: {Concurrent} concurrent requests, each blocking for {Delay}ms. " +
            "Thread pool has {Available}/{Max} workers available. THIS WILL CAUSE THREAD STARVATION!",
            simulationId,
            actualConcurrent,
            actualDelay,
            workerThreads,
            maxWorker);

        // ==========================================================================
        // STEP 2: Start the blocking operations in the background
        // ==========================================================================
        _ = Task.Run(() => ExecuteThreadBlocking(simulationId, actualDelay, actualConcurrent, cts.Token), cts.Token);

        // ==========================================================================
        // STEP 3: Return immediately with simulation info
        // ==========================================================================
        var result = new SimulationResult
        {
            SimulationId = simulationId,
            Type = SimulationType.ThreadBlock,
            Status = "Started",
            Message = $"Started {actualConcurrent} concurrent sync-over-async blocking operations, each waiting {actualDelay}ms. " +
                      $"Thread pool currently has {workerThreads} available worker threads. " +
                      "Watch for increased response times on other endpoints as threads become blocked. " +
                      "This demonstrates why you should NEVER use .Result or .Wait() on Tasks in ASP.NET - " +
                      "it blocks threads that could be handling other requests!",
            ActualParameters = parameters,
            StartedAt = startedAt,
            EstimatedEndAt = estimatedEndAt
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Executes the sync-over-async blocking operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>⚠️⚠️⚠️ THIS IS THE ANTI-PATTERN - NEVER DO THIS ⚠️⚠️⚠️</strong>
    /// </para>
    /// <para>
    /// The code below uses <c>Task.Delay(ms).Result</c> which is the classic example
    /// of sync-over-async. Each call to <c>.Result</c>:
    /// <list type="number">
    /// <item>Starts the async delay operation</item>
    /// <item>BLOCKS the current thread waiting for it</item>
    /// <item>The thread cannot do ANY other work while blocked</item>
    /// <item>When many threads are blocked, no threads are available for new requests</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>The correct way:</strong>
    /// <code>
    /// await Task.Delay(ms);  // ✅ Releases thread while waiting
    /// </code>
    /// </para>
    /// </remarks>
    private void ExecuteThreadBlocking(Guid simulationId, int delayMs, int concurrentRequests, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Simulation {SimulationId}: Spawning {Count} blocking operations",
                simulationId,
                concurrentRequests);

            // Create tasks that each block a thread pool thread
            var tasks = new Task[concurrentRequests];

            for (int i = 0; i < concurrentRequests; i++)
            {
                var requestNumber = i + 1;

                // Each Task.Run gets a thread pool thread, then BLOCKS it
                tasks[i] = Task.Run(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    _logger.LogDebug(
                        "Simulation {SimulationId}: Request {Number} blocking thread for {Delay}ms",
                        simulationId,
                        requestNumber,
                        delayMs);

                    // ==========================================================
                    // ⚠️⚠️⚠️ THIS IS THE ANTI-PATTERN ⚠️⚠️⚠️
                    // ==========================================================
                    // Using .Wait() on Task.Delay BLOCKS the current thread.
                    // This thread cannot do ANY work while waiting.
                    // If all threads are blocked like this, the app appears hung.
                    //
                    // NEVER DO THIS IN PRODUCTION!
                    //
                    // Instead, use: await Task.Delay(delayMs, cancellationToken);
                    // ==========================================================
                    try
                    {
                        Task.Delay(delayMs, cancellationToken).Wait(); // ❌ BAD!
                    }
                    catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
                    {
                        // Cancellation is expected
                    }

                    _logger.LogDebug(
                        "Simulation {SimulationId}: Request {Number} unblocked",
                        simulationId,
                        requestNumber);
                }, cancellationToken);
            }

            // Wait for all blocking operations to complete
            try
            {
                Task.WaitAll(tasks, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Simulation {SimulationId} was cancelled", simulationId);
            }

            _logger.LogInformation(
                "Simulation {SimulationId}: All {Count} blocking operations completed",
                simulationId,
                concurrentRequests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulation {SimulationId} failed with error", simulationId);
        }
        finally
        {
            _simulationTracker.UnregisterSimulation(simulationId);
        }
    }
}
