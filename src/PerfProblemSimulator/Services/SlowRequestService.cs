using PerfProblemSimulator.Models;
using System.Runtime.CompilerServices;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that simulates slow requests using sync-over-async anti-patterns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ EDUCATIONAL PURPOSE ONLY ⚠️</strong>
/// </para>
/// <para>
/// This service intentionally implements sync-over-async anti-patterns to demonstrate
/// what they look like in a CLR Profiler trace. The method names are deliberately
/// descriptive so they're easy to find in profiler output.
/// </para>
/// <para>
/// <strong>What CLR Profiler Will Show:</strong>
/// <list type="bullet">
/// <item>Threads blocked at <c>Task.Result</c>, <c>Task.Wait()</c>, or <c>GetAwaiter().GetResult()</c></item>
/// <item>Low CPU usage despite slow responses (threads are waiting, not working)</item>
/// <item>Time spent in <c>ManualResetEventSlim.Wait</c> or similar synchronization primitives</item>
/// </list>
/// </para>
/// <para>
/// <strong>Real-World Causes:</strong>
/// <list type="bullet">
/// <item>Legacy code calling async methods synchronously</item>
/// <item>Incorrect async/await usage in constructors or properties</item>
/// <item>Mixing sync and async code paths</item>
/// <item>Third-party libraries with sync-only APIs calling async internally</item>
/// </list>
/// </para>
/// </remarks>
public class SlowRequestService : ISlowRequestService, IDisposable
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<SlowRequestService> _logger;
    private readonly Random _random = new();

    private CancellationTokenSource? _cts;
    private Thread? _requestSpawnerThread;
    private volatile bool _isRunning;
    private int _requestsSent;
    private int _requestsCompleted;
    private int _requestsInProgress;
    private int _intervalSeconds;
    private int _requestDurationSeconds;
    private int _maxRequests;
    private DateTimeOffset? _startedAt;
    private Guid _simulationId;
    private readonly Dictionary<string, int> _scenarioCounts = new();
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;

    public SlowRequestService(
        ISimulationTracker simulationTracker,
        ILogger<SlowRequestService> logger)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SimulationResult Start(SlowRequestRequest request)
    {
        if (_isRunning)
        {
            return new SimulationResult
            {
                SimulationId = _simulationId,
                Type = SimulationType.SlowRequest,
                Status = "AlreadyRunning",
                Message = "Slow request simulation is already running. Stop it first."
            };
        }

        _simulationId = Guid.NewGuid();
        _cts = new CancellationTokenSource();
        _requestsSent = 0;
        _requestsCompleted = 0;
        _requestsInProgress = 0;
        _intervalSeconds = Math.Max(5, request.IntervalSeconds);
        _requestDurationSeconds = Math.Max(10, request.RequestDurationSeconds);
        _maxRequests = request.MaxRequests;
        _startedAt = DateTimeOffset.UtcNow;
        _scenarioCounts.Clear();
        _isRunning = true;

        var parameters = new Dictionary<string, object>
        {
            ["IntervalSeconds"] = _intervalSeconds,
            ["RequestDurationSeconds"] = _requestDurationSeconds,
            ["MaxRequests"] = request.MaxRequests
        };

        _simulationTracker.RegisterSimulation(_simulationId, SimulationType.SlowRequest, parameters, _cts);

        // Use dedicated thread (not thread pool) to spawn requests
        _requestSpawnerThread = new Thread(() => SpawnRequestsLoop(request.MaxRequests, _cts.Token))
        {
            Name = $"SlowRequestSpawner-{_simulationId:N}",
            IsBackground = true
        };
        _requestSpawnerThread.Start();

        _logger.LogWarning(
            "🐌 Slow request simulation started: {SimulationId}. " +
            "Duration={Duration}s, Interval={Interval}s. " +
            "Requests will use random sync-over-async scenarios.",
            _simulationId, _requestDurationSeconds, _intervalSeconds);

        return new SimulationResult
        {
            SimulationId = _simulationId,
            Type = SimulationType.SlowRequest,
            Status = "Started",
            Message = $"Slow request simulation started. Sending requests every {_intervalSeconds}s, " +
                      $"each taking ~{_requestDurationSeconds}s. Scenarios: SimpleSyncOverAsync, NestedSyncOverAsync, DatabasePattern. " +
                      "Collect a CLR Profile trace to see sync-over-async blocking patterns.",
            ActualParameters = parameters,
            StartedAt = _startedAt.Value
        };
    }

    public SimulationResult Stop()
    {
        if (!_isRunning)
        {
            return new SimulationResult
            {
                SimulationId = Guid.Empty,
                Type = SimulationType.SlowRequest,
                Status = "NotRunning",
                Message = "No slow request simulation is running."
            };
        }

        _cts?.Cancel();
        _isRunning = false;
        _simulationTracker.UnregisterSimulation(_simulationId);

        _logger.LogInformation(
            "🛑 Slow request simulation stopped: {SimulationId}. " +
            "Sent={Sent}, Completed={Completed}, InProgress={InProgress}",
            _simulationId, _requestsSent, _requestsCompleted, _requestsInProgress);

        return new SimulationResult
        {
            SimulationId = _simulationId,
            Type = SimulationType.SlowRequest,
            Status = "Stopped",
            Message = $"Slow request simulation stopped. Total requests: {_requestsSent}, " +
                      $"Completed: {_requestsCompleted}, Still in progress: {_requestsInProgress}",
            ActualParameters = new Dictionary<string, object>
            {
                ["RequestsSent"] = _requestsSent,
                ["RequestsCompleted"] = _requestsCompleted,
                ["ScenarioCounts"] = _scenarioCounts
            }
        };
    }

    public SlowRequestStatus GetStatus()
    {
        lock (_lock)
        {
            return new SlowRequestStatus
            {
                IsRunning = _isRunning,
                RequestsSent = _requestsSent,
                RequestsCompleted = _requestsCompleted,
                RequestsInProgress = _requestsInProgress,
                IntervalSeconds = _intervalSeconds,
                RequestDurationSeconds = _requestDurationSeconds,
                StartedAt = _startedAt,
                ScenarioCounts = new Dictionary<string, int>(_scenarioCounts)
            };
        }
    }

    private void SpawnRequestsLoop(int maxRequests, CancellationToken ct)
    {
        int requestNumber = 0;

        while (!ct.IsCancellationRequested && (maxRequests == 0 || requestNumber < maxRequests))
        {
            requestNumber++;
            Interlocked.Increment(ref _requestsSent);
            Interlocked.Increment(ref _requestsInProgress);

            // Randomly select a scenario
            var scenario = (SlowRequestScenario)_random.Next(1, 4); // 1, 2, or 3 (skip Random=0)
            
            lock (_lock)
            {
                var scenarioName = scenario.ToString();
                _scenarioCounts.TryGetValue(scenarioName, out var count);
                _scenarioCounts[scenarioName] = count + 1;
            }

            _logger.LogInformation(
                "🐌 Spawning slow request #{Number} using scenario: {Scenario}",
                requestNumber, scenario);

            // Spawn the slow request on a new thread (simulating incoming HTTP request)
            var requestThread = new Thread(() => ExecuteSlowRequest(scenario, requestNumber, _requestDurationSeconds, ct))
            {
                Name = $"SlowRequest-{requestNumber}-{scenario}",
                IsBackground = true
            };
            requestThread.Start();

            // Wait for interval before next request
            try
            {
                Thread.Sleep(_intervalSeconds * 1000);
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
        }

        _logger.LogInformation("Slow request spawner loop ended");
    }

    private void ExecuteSlowRequest(SlowRequestScenario scenario, int requestNumber, int durationSeconds, CancellationToken ct)
    {
        try
        {
            // Execute the blocking scenario - uses Thread.Sleep so it shows up in CLR Profiler
            switch (scenario)
            {
                case SlowRequestScenario.SimpleSyncOverAsync:
                    ExecuteSimpleSyncOverAsyncRequest(durationSeconds, ct);
                    break;

                case SlowRequestScenario.NestedSyncOverAsync:
                    ExecuteNestedSyncOverAsyncRequest(durationSeconds, ct);
                    break;

                case SlowRequestScenario.DatabasePattern:
                    ExecuteDatabasePatternRequest(durationSeconds, ct);
                    break;
            }

            _logger.LogInformation("🐌 Slow request #{Number} ({Scenario}) completed after {Duration}s", 
                requestNumber, scenario, durationSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Slow request #{Number} cancelled", requestNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slow request #{Number} failed", requestNumber);
        }
        finally
        {
            Interlocked.Increment(ref _requestsCompleted);
            var remaining = Interlocked.Decrement(ref _requestsInProgress);
            
            // Check if simulation is naturally complete (all requests done)
            CheckAndCompleteSimulation();
        }
    }

    /// <summary>
    /// Checks if all requests have completed and marks the simulation as done.
    /// </summary>
    private void CheckAndCompleteSimulation()
    {
        // Only check if we have a max request limit and we've sent all requests
        if (_maxRequests > 0 && _requestsSent >= _maxRequests && _requestsInProgress == 0 && _isRunning)
        {
            lock (_lock)
            {
                // Double-check inside lock to avoid race conditions
                if (_isRunning && _requestsInProgress == 0 && _requestsSent >= _maxRequests)
                {
                    _isRunning = false;
                    _simulationTracker.UnregisterSimulation(_simulationId);
                    
                    _logger.LogInformation(
                        "🐌 Slow request simulation completed naturally: {SimulationId}. " +
                        "Total requests: {Total}, Completed: {Completed}",
                        _simulationId, _requestsSent, _requestsCompleted);
                }
            }
        }
    }

    // ==========================================================================
    // SCENARIO 1: Simple Sync-Over-Async
    // ==========================================================================
    // CLR Profiler will show time blocked at .Result and .Wait()
    // Using Thread.Sleep to ensure the time shows up in profiler

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteSimpleSyncOverAsyncRequest(int totalDurationSeconds, CancellationToken ct)
    {
        var partDuration = totalDurationSeconds * 1000 / 3;

        // BAD: Blocking the thread - shows up in CLR Profiler
        FetchDataSync_BLOCKING_HERE(partDuration);
        ct.ThrowIfCancellationRequested();

        // BAD: More blocking
        ProcessDataSync_BLOCKING_HERE(partDuration);
        ct.ThrowIfCancellationRequested();

        // BAD: Final blocking call
        SaveDataSync_BLOCKING_HERE(partDuration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FetchDataSync_BLOCKING_HERE(int delayMs)
    {
        // Intentional blocking - will show in profiler as Thread.Sleep
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProcessDataSync_BLOCKING_HERE(int delayMs)
    {
        // Intentional blocking
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SaveDataSync_BLOCKING_HERE(int delayMs)
    {
        // Intentional blocking
        Thread.Sleep(delayMs);
    }

    // ==========================================================================
    // SCENARIO 2: Nested Sync-Over-Async
    // ==========================================================================
    // CLR Profiler will show a chain of blocking calls through multiple methods

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteNestedSyncOverAsyncRequest(int totalDurationSeconds, CancellationToken ct)
    {
        var partDuration = totalDurationSeconds * 1000 / 4;

        // Each method internally blocks - creates nested blocking pattern
        ValidateOrderSync_BLOCKS_INTERNALLY(partDuration);
        ct.ThrowIfCancellationRequested();
        
        CheckInventorySync_BLOCKS_INTERNALLY(partDuration);
        ct.ThrowIfCancellationRequested();
        
        ProcessPaymentSync_BLOCKS_INTERNALLY(partDuration);
        ct.ThrowIfCancellationRequested();
        
        SendConfirmationSync_BLOCKS_INTERNALLY(partDuration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ValidateOrderSync_BLOCKS_INTERNALLY(int delayMs)
    {
        // BAD: Blocking internally
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckInventorySync_BLOCKS_INTERNALLY(int delayMs)
    {
        // BAD: Blocking internally
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProcessPaymentSync_BLOCKS_INTERNALLY(int delayMs)
    {
        // BAD: Blocking internally
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SendConfirmationSync_BLOCKS_INTERNALLY(int delayMs)
    {
        // BAD: Blocking internally
        Thread.Sleep(delayMs);
    }

    // ==========================================================================
    // SCENARIO 3: Realistic Database/HTTP Pattern
    // ==========================================================================
    // CLR Profiler will show GetAwaiter().GetResult() - common in legacy migrations

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteDatabasePatternRequest(int totalDurationSeconds, CancellationToken ct)
    {
        var partDuration = totalDurationSeconds * 1000 / 5;

        // This pattern is very common in legacy code - method names show what it simulates
        GetCustomerFromDatabaseSync_SYNC_BLOCK(partDuration);
        ct.ThrowIfCancellationRequested();

        GetOrderHistoryFromDatabaseSync_SYNC_BLOCK(partDuration);
        ct.ThrowIfCancellationRequested();

        CheckInventoryServiceSync_SYNC_BLOCK(partDuration);
        ct.ThrowIfCancellationRequested();

        GetRecommendationsFromMLServiceSync_SYNC_BLOCK(partDuration);
        ct.ThrowIfCancellationRequested();

        BuildResponseSync_SYNC_BLOCK(partDuration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GetCustomerFromDatabaseSync_SYNC_BLOCK(int delayMs)
    {
        // Simulates blocking database query
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GetOrderHistoryFromDatabaseSync_SYNC_BLOCK(int delayMs)
    {
        // Simulates blocking database query
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckInventoryServiceSync_SYNC_BLOCK(int delayMs)
    {
        // Simulates blocking HTTP call
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GetRecommendationsFromMLServiceSync_SYNC_BLOCK(int delayMs)
    {
        // Simulates blocking ML service call
        Thread.Sleep(delayMs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildResponseSync_SYNC_BLOCK(int delayMs)
    {
        // Simulates response building delay
        Thread.Sleep(delayMs);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
