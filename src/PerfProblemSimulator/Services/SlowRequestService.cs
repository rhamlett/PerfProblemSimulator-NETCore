using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that simulates slow requests with intentional blocking patterns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ EDUCATIONAL PURPOSE ONLY ⚠️</strong>
/// </para>
/// <para>
/// This service intentionally implements blocking patterns using Thread.Sleep to demonstrate
/// what slow requests look like in a CLR Profiler trace. The method names are deliberately
/// descriptive so they're easy to find in profiler output.
/// </para>
/// <para>
/// <strong>What CLR Profiler Will Show:</strong>
/// <list type="bullet">
/// <item>Time spent in <c>Thread.Sleep</c> calls - clearly visible as method self-time</item>
/// <item>Low CPU usage despite slow responses (threads are sleeping, not working)</item>
/// <item>Blocking calls that consume thread pool threads</item>
/// </list>
/// </para>
/// <para>
/// <strong>Real-World Equivalent Causes:</strong>
/// <list type="bullet">
/// <item>Database queries with long timeouts</item>
/// <item>External HTTP calls to slow services</item>
/// <item>File I/O operations on slow storage</item>
/// <item>Sync-over-async anti-patterns (Task.Wait(), .Result, GetAwaiter().GetResult())</item>
/// </list>
/// </para>
/// </remarks>
public class SlowRequestService : ISlowRequestService, IDisposable
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ILogger<SlowRequestService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly IServer _server;
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
    private string? _baseUrl;

    public bool IsRunning => _isRunning;

    public SlowRequestService(
        ISimulationTracker simulationTracker,
        ILogger<SlowRequestService> logger,
        IHttpClientFactory httpClientFactory,
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        IServer server)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _server = server ?? throw new ArgumentNullException(nameof(server));
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
        _intervalSeconds = Math.Max(1, request.IntervalSeconds);  // Allow 1 second minimum
        _requestDurationSeconds = Math.Max(5, request.RequestDurationSeconds);  // Allow 5 second minimum
        _maxRequests = request.MaxRequests;
        _startedAt = DateTimeOffset.UtcNow;
        _scenarioCounts.Clear();
        _isRunning = true;
        
        // Get the base URL for HTTP calls
        _baseUrl = GetBaseUrl();

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
            "Making HTTP calls to {BaseUrl}/api/slowrequest/execute-slow",
            _simulationId, _requestDurationSeconds, _intervalSeconds, _baseUrl);

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
        // =========================================================================================
        // WAIT FOR EXISTING PROBES TO DRAIN
        // IsSimulationActive(SlowRequest) is already true (set in Start method).
        // This causes new probes to be skipped (see LatencyProbeService).
        // Now we wait a moment for any *existing* probes in the pipeline to finish 
        // before we start generating noise. This ensures a clean CLR profile trace.
        // =========================================================================================
        _logger.LogInformation("⏳ Waiting 3 seconds for existing health probes to drain...");
        Thread.Sleep(3000);

        int requestNumber = 0;

        while (!ct.IsCancellationRequested && (maxRequests == 0 || requestNumber < maxRequests))
        {
            requestNumber++;
            Interlocked.Increment(ref _requestsSent);
            Interlocked.Increment(ref _requestsInProgress);

            // Randomly select a scenario for logging purposes
            var scenario = (SlowRequestScenario)_random.Next(1, 4); // 1, 2, or 3 (skip Random=0)
            
            lock (_lock)
            {
                var scenarioName = scenario.ToString();
                _scenarioCounts.TryGetValue(scenarioName, out var count);
                _scenarioCounts[scenarioName] = count + 1;
            }

            _logger.LogInformation(
                "🐌 Spawning slow HTTP request #{Number} to {BaseUrl}/api/slowrequest/execute-slow?durationSeconds={Duration}",
                requestNumber, _baseUrl, _requestDurationSeconds);

            // Spawn the slow request on a new thread - this will make an HTTP call
            var reqNum = requestNumber;
            var requestThread = new Thread(() => ExecuteSlowHttpRequest(scenario, reqNum, _requestDurationSeconds, ct))
            {
                Name = $"SlowRequest-{requestNumber}-{scenario}",
                IsBackground = true
            };
            requestThread.Start();

            // Wait for interval before next request
            try
            {
                // Thread.Sleep(_intervalSeconds * 1000);
                
                // FIX: Instead of one long sleep (which causes silence in ETW traces), 
                // we sleep in small chunks and log "heartbeats". 
                // This ensures the ETW buffers fill up and flush to the .diagsession file,
                // allowing the CLR Profiler to see the "Request Finished" events even if the app is otherwise idle.
                for (int i = 0; i < _intervalSeconds * 2; i++) // 500ms chunks
                {
                    if (ct.IsCancellationRequested) break;
                    Thread.Sleep(500);
                    // Log trace (verbose) - high volume event to force buffer flush
                    _logger.LogTrace("Generating trace noise to flush ETW buffers...");
                }
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
        }

        _logger.LogInformation("Slow request spawner loop ended");
    }

    /// <summary>
    /// Makes an HTTP call to the slow request endpoint - this goes through the ASP.NET pipeline
    /// and will show up in the Request Latency Monitor.
    /// </summary>
    private void ExecuteSlowHttpRequest(SlowRequestScenario scenario, int requestNumber, int durationSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                _logger.LogError("Base URL not configured for slow request HTTP calls");
                return;
            }

            var client = _httpClientFactory.CreateClient("SlowRequest");
            client.Timeout = TimeSpan.FromSeconds(durationSeconds + 30); // Extra buffer for network overhead
            
            var url = $"{_baseUrl}/api/slowrequest/execute-slow?durationSeconds={durationSeconds}&scenario={scenario}";
            
            // Make HTTP call - this blocks the thread and goes through ASP.NET pipeline
            var response = client.GetAsync(url, ct).GetAwaiter().GetResult();
            
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "🐌 Slow HTTP request #{Number} failed with status {StatusCode} after {Latency:F0}ms", 
                    requestNumber, response.StatusCode, latencyMs);
            }
            else
            {
                _logger.LogInformation(
                    "🐌 Slow HTTP request #{Number} ({Scenario}) completed in {Latency:F0}ms (expected ~{Expected}s)", 
                    requestNumber, scenario, latencyMs, durationSeconds);
            }

            // Broadcast the slow request latency to the dashboard
            BroadcastSlowRequestLatency(requestNumber, scenario, latencyMs, durationSeconds * 1000);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;
            _logger.LogWarning("Slow request #{Number} timed out after {Latency:F0}ms", requestNumber, latencyMs);
            
            BroadcastSlowRequestLatency(requestNumber, scenario, latencyMs, durationSeconds * 1000, 
                isError: true, 
                errorMessage: "Request Timed Out (Client Side)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;
            _logger.LogError(ex, "Slow HTTP request #{Number} failed after {Elapsed}ms", requestNumber, latencyMs);
            
            BroadcastSlowRequestLatency(requestNumber, scenario, latencyMs, durationSeconds * 1000, 
                isError: true, 
                errorMessage: ex.Message);
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
    /// Broadcasts slow request latency to connected dashboard clients.
    /// </summary>
    private void BroadcastSlowRequestLatency(int requestNumber, SlowRequestScenario scenario, double latencyMs, double expectedDurationMs, bool isError = false, string? errorMessage = null)
    {
        try
        {
            _hubContext.Clients.All.ReceiveSlowRequestLatency(new SlowRequestLatencyData
            {
                RequestNumber = requestNumber,
                Scenario = scenario.ToString(),
                LatencyMs = latencyMs,
                ExpectedDurationMs = expectedDurationMs,
                Timestamp = DateTimeOffset.UtcNow,
                IsError = isError,
                ErrorMessage = errorMessage
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to broadcast slow request latency");
        }
    }
    
    /// <summary>
    /// Gets the base URL for making HTTP calls to ourselves.
    /// </summary>
    private string? GetBaseUrl()
    {
        try
        {
            var addresses = _server.Features.Get<IServerAddressesFeature>();
            if (addresses != null && addresses.Addresses.Count > 0)
            {
                var address = addresses.Addresses.First();
                // Replace wildcard addresses
                address = address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");
                _logger.LogDebug("Detected server address for slow requests: {Address}", address);
                return address;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect server address");
        }
        
        // Fallback for Azure/production environments - use relative URL
        return "http://localhost:5021";
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
    // SCENARIO 1: Simple Blocking
    // ==========================================================================
    // CLR Profiler will show time spent in Thread.Sleep
    // This simulates slow I/O operations like database queries or HTTP calls

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
    // SCENARIO 2: Nested Blocking Methods
    // ==========================================================================
    // CLR Profiler will show a chain of blocking calls through multiple methods
    // Each method internally uses Thread.Sleep to simulate work

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
    // CLR Profiler will show time spent in simulated database and HTTP calls
    // This simulates what blocking I/O looks like in production code

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
