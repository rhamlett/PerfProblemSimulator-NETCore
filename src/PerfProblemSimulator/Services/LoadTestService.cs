/*
 * =============================================================================
 * LOAD TEST SERVICE IMPLEMENTATION - AI PORTABILITY DOCUMENTATION
 * =============================================================================
 * 
 * PURPOSE:
 * This is the core implementation of the load test feature. It contains the
 * algorithm that simulates realistic application behavior under load.
 * 
 * ALGORITHM SUMMARY:
 * ┌─────────────────────────────────────────────────────────────────────────┐
 * │ 1. INCREMENT concurrent counter (thread-safe)                          │
 * │ 2. START stopwatch                                                      │
 * │ 3. CALCULATE degradation delay:                                         │
 * │    delay = max(0, concurrent - softLimit) * degradationFactor           │
 * │ 4. APPLY delay (if any)                                                 │
 * │ 5. PERFORM CPU work (hash iterations)                                   │
 * │ 6. ALLOCATE memory buffer (released on method exit)                     │
 * │ 7. LOOP while processing:                                               │
 * │    - Check elapsed time                                                 │
 * │    - If > 120s: 20% chance to throw random exception                    │
 * │ 8. DECREMENT concurrent counter (in finally block - always runs)        │
 * │ 9. RETURN result with timing details                                    │
 * └─────────────────────────────────────────────────────────────────────────┘
 * 
 * THREAD SAFETY:
 * Uses Interlocked methods for atomic counter operations. This is critical
 * because multiple requests execute concurrently.
 * 
 * PORTING TO OTHER LANGUAGES:
 * - PHP: Use APCu/Redis for shared state (PHP processes don't share memory)
 * - Node.js: Simple counter works (single-threaded), use atomic for workers
 * - Java: AtomicInteger, AtomicLong for counters
 * - Python: threading.Lock or asyncio.Lock, or multiprocessing.Value
 * 
 * DEPENDENCIES:
 * - Models/LoadTestRequest.cs - Input parameters
 * - Models/LoadTestResult.cs - Output format
 * - System.Security.Cryptography - For SHA256 hash work
 * - System.Diagnostics - For Stopwatch timing
 * 
 * =============================================================================
 */

using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Implementation of load test service that simulates realistic application
/// behavior under varying load conditions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>DESIGN RATIONALE:</strong>
/// </para>
/// <para>
/// This service is designed to provide a realistic target for load testing tools
/// like Azure Load Testing. Unlike simple echo endpoints, this simulates real
/// application behavior:
/// </para>
/// <list type="bullet">
/// <item>Performs actual CPU work (not just sleeping)</item>
/// <item>Allocates real memory (garbage collected after request)</item>
/// <item>Degrades naturally under load (soft limit pattern)</item>
/// <item>Fails realistically after extended processing (random exceptions)</item>
/// </list>
/// <para>
/// <strong>WHY SOFT LIMIT INSTEAD OF HARD LIMIT:</strong>
/// </para>
/// <para>
/// A hard limit would reject requests above a threshold. Real applications don't
/// work this way - they slow down gradually. The soft limit creates a realistic
/// degradation curve where latency increases proportionally to load.
/// </para>
/// </remarks>
public class LoadTestService : ILoadTestService
{
    /*
     * =========================================================================
     * THREAD-SAFE COUNTERS
     * =========================================================================
     * 
     * CONCEPT: Atomic Operations
     * When multiple threads modify a variable, we need "atomic" operations that
     * complete as a single unit. Without this, two threads might read the same
     * value and both increment to the same result, losing one increment.
     * 
     * C# IMPLEMENTATION:
     * - Interlocked.Increment: Atomically adds 1 and returns new value
     * - Interlocked.Decrement: Atomically subtracts 1 and returns new value
     * - Interlocked.Read: Atomically reads a 64-bit value
     * 
     * PORTING:
     * - PHP: APCu (apcu_inc/apcu_dec) or Redis (INCR/DECR) for cross-process
     * - Node.js: Standard increment works (single event loop)
     *            For worker threads: Atomics.add(sharedArray, index, 1)
     * - Java: AtomicInteger counter = new AtomicInteger();
     *         counter.incrementAndGet(); counter.decrementAndGet();
     * - Python: threading.Lock() with manual increment, or atomic-counter package
     *           import threading
     *           lock = threading.Lock()
     *           with lock: counter += 1
     */
    
    /// <summary>
    /// Current number of requests being processed. Thread-safe via Interlocked.
    /// </summary>
    private int _concurrentRequests = 0;
    
    /// <summary>
    /// Total requests processed since service start. Thread-safe via Interlocked.
    /// </summary>
    private long _totalRequestsProcessed = 0;
    
    /// <summary>
    /// Total exceptions thrown (when requests exceed 120s). Thread-safe via Interlocked.
    /// </summary>
    private long _totalExceptionsThrown = 0;
    
    /// <summary>
    /// Running sum of response times for average calculation. Thread-safe via Interlocked.
    /// </summary>
    private long _totalResponseTimeMs = 0;
    
    /*
     * =========================================================================
     * PERIOD STATS FOR EVENT LOG BROADCASTING
     * =========================================================================
     * 
     * These fields track statistics for the current 60-second reporting period.
     * They are reset after each broadcast to the event log.
     */
    
    /// <summary>
    /// Requests completed in the current 60-second period.
    /// </summary>
    private long _periodRequestsCompleted = 0;
    
    /// <summary>
    /// Sum of response times in the current period (for average calculation).
    /// </summary>
    private long _periodResponseTimeSum = 0;
    
    /// <summary>
    /// Maximum response time observed in the current period.
    /// </summary>
    private long _periodMaxResponseTimeMs = 0;
    
    /// <summary>
    /// Peak concurrent requests observed in the current period.
    /// </summary>
    private int _periodPeakConcurrent = 0;
    
    /// <summary>
    /// Exceptions thrown in the current period.
    /// </summary>
    private long _periodExceptions = 0;
    
    /// <summary>
    /// Timer for broadcasting stats to event log every 60 seconds.
    /// </summary>
    private Timer? _broadcastTimer;
    
    /// <summary>
    /// Tracks when load test traffic started for this period.
    /// </summary>
    private DateTimeOffset _periodStartTime = DateTimeOffset.UtcNow;

    /*
     * =========================================================================
     * RANDOM EXCEPTION POOL
     * =========================================================================
     * 
     * DESIGN DECISION:
     * Instead of a custom exception type, we throw random .NET exceptions to
     * simulate realistic application failures. Real apps fail in diverse ways.
     * 
     * EXCEPTION SELECTION:
     * These are common exceptions you'd see in production applications.
     * Each simulates a different failure mode.
     * 
     * PORTING:
     * Create equivalent exception types in your target language:
     * - PHP: new InvalidArgumentException(), new RuntimeException(), etc.
     * - Node.js: new Error(), new TypeError(), new RangeError(), etc.
     * - Java: new IllegalArgumentException(), new NullPointerException(), etc.
     * - Python: ValueError(), TypeError(), RuntimeError(), TimeoutError(), etc.
     */
    
    /// <summary>
    /// Pool of exception types to randomly throw after timeout threshold.
    /// Simulates diverse real-world application failures.
    /// </summary>
    private static readonly Func<Exception>[] ExceptionFactories = new Func<Exception>[]
    {
        // Common application logic exceptions
        () => new InvalidOperationException("Operation is not valid due to current state"),
        () => new ArgumentException("Value does not fall within the expected range"),
        () => new ArgumentNullException("value", "Value cannot be null"),
        
        // Classic .NET exceptions (the ones everyone loves to hate)
        () => new NullReferenceException("Object reference not set to an instance of an object"),
        () => new IndexOutOfRangeException("Index was outside the bounds of the array"),
        () => new KeyNotFoundException("The given key was not present in the dictionary"),
        
        // I/O and network-related exceptions
        () => new TimeoutException("The operation has timed out"),
        () => new IOException("Unable to read data from the transport connection"),
        () => new HttpRequestException("An error occurred while sending the request"),
        
        // Math and format exceptions
        () => new DivideByZeroException("Attempted to divide by zero"),
        () => new FormatException("Input string was not in a correct format"),
        () => new OverflowException("Arithmetic operation resulted in an overflow"),
        
        // Task-related exceptions
        () => new TaskCanceledException("A task was canceled"),
        () => new OperationCanceledException("The operation was canceled"),
        
        // Scary ones (use sparingly in real scenarios)
        () => new OutOfMemoryException("Insufficient memory to continue execution"),
        () => new StackOverflowException() // Note: This one is usually uncatchable!
    };
    
    /*
     * =========================================================================
     * CONFIGURATION CONSTANTS
     * =========================================================================
     * 
     * These could be moved to appsettings.json for configurability.
     * Keeping as constants for simplicity and AI portability.
     */
    
    /// <summary>
    /// Time threshold in seconds after which exceptions may be thrown.
    /// </summary>
    /// <remarks>
    /// DESIGN RATIONALE:
    /// 120 seconds is chosen because:
    /// - Azure App Service default timeout is 230 seconds
    /// - 120s gives enough time for meaningful load testing data
    /// - Leaves 110s buffer before Azure timeout kicks in
    /// </remarks>
    private const int ExceptionThresholdSeconds = 120;
    
    /// <summary>
    /// Probability (0.0 to 1.0) of throwing exception after threshold.
    /// </summary>
    /// <remarks>
    /// 20% (0.20) means roughly 1 in 5 requests that exceed 120s will fail.
    /// This creates realistic sporadic failures under extreme load.
    /// </remarks>
    private const double ExceptionProbability = 0.20;
    
    /// <summary>
    /// Interval in milliseconds between exception probability checks.
    /// </summary>
    /// <remarks>
    /// During degradation delays, we check every 1000ms (1 second) whether
    /// to throw an exception. This prevents constant checking overhead
    /// while still being responsive to the timeout threshold.
    /// </remarks>
    private const int ExceptionCheckIntervalMs = 1000;
    
    /// <summary>
    /// Interval in seconds between event log broadcasts.
    /// </summary>
    private const int BroadcastIntervalSeconds = 60;

    private readonly ILogger<LoadTestService> _logger;
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the LoadTestService.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="hubContext">SignalR hub context for broadcasting stats.</param>
    public LoadTestService(
        ILogger<LoadTestService> logger, 
        IHubContext<MetricsHub, IMetricsClient> hubContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        
        // Start timer for periodic broadcasting (fires every 60 seconds)
        _broadcastTimer = new Timer(
            callback: BroadcastStats,
            state: null,
            dueTime: TimeSpan.FromSeconds(BroadcastIntervalSeconds),
            period: TimeSpan.FromSeconds(BroadcastIntervalSeconds));
    }
    
    /// <summary>
    /// Timer callback that broadcasts load test stats to event log every 60 seconds.
    /// Only broadcasts if there was activity during the period.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Fire-and-Forget Pattern:</strong>
    /// </para>
    /// <para>
    /// SignalR's SendAsync needs thread pool threads for I/O completion. If we block
    /// waiting (GetAwaiter().GetResult()), we deadlock when the pool is exhausted.
    /// Instead, we fire the send without waiting - the message will be delivered
    /// as soon as any thread pool thread becomes available.
    /// </para>
    /// </remarks>
    private void BroadcastStats(object? state)
    {
        try
        {
            // Read and reset period stats atomically
            var requestsCompleted = Interlocked.Exchange(ref _periodRequestsCompleted, 0);
            
            _logger.LogInformation(
                "Load test timer fired - requests in period: {Requests}, currentConcurrent: {Concurrent}",
                requestsCompleted,
                _concurrentRequests);
            
            // Only broadcast if there was activity
            if (requestsCompleted == 0)
            {
                return;
            }
            
            var responseTimeSum = Interlocked.Exchange(ref _periodResponseTimeSum, 0);
            var maxResponseTime = Interlocked.Exchange(ref _periodMaxResponseTimeMs, 0);
            var peakConcurrent = Interlocked.Exchange(ref _periodPeakConcurrent, 0);
            var exceptions = Interlocked.Exchange(ref _periodExceptions, 0);
            var currentConcurrent = Interlocked.CompareExchange(ref _concurrentRequests, 0, 0);
            
            // Calculate averages
            var avgResponseTime = requestsCompleted > 0 
                ? (double)responseTimeSum / requestsCompleted 
                : 0;
            var requestsPerSecond = (double)requestsCompleted / BroadcastIntervalSeconds;
            
            var statsData = new LoadTestStatsData
            {
                CurrentConcurrent = currentConcurrent,
                PeakConcurrent = peakConcurrent,
                RequestsCompleted = requestsCompleted,
                AvgResponseTimeMs = Math.Round(avgResponseTime, 2),
                MaxResponseTimeMs = maxResponseTime,
                RequestsPerSecond = Math.Round(requestsPerSecond, 2),
                ExceptionCount = (int)exceptions,
                Timestamp = DateTimeOffset.UtcNow
            };
            
            _logger.LogInformation(
                "Broadcasting load test stats: {Requests} requests, {AvgMs}ms avg, {MaxMs}ms max, {RPS} RPS",
                requestsCompleted, avgResponseTime, maxResponseTime, requestsPerSecond);
            
            // Fire-and-forget: don't wait for SignalR completion (would deadlock during thread pool starvation)
            _ = _hubContext.Clients.All.ReceiveLoadTestStats(statsData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BroadcastStats timer callback");
        }
        
        // Reset period start time
        _periodStartTime = DateTimeOffset.UtcNow;
    }

    /*
     * =========================================================================
     * MAIN ALGORITHM: ExecuteWorkAsync
     * =========================================================================
     * 
     * This is the core method that implements the load test behavior.
     * 
     * PSEUDOCODE (language-agnostic):
     * 
     *   function executeWork(request):
     *       concurrent = atomicIncrement(concurrentCounter)
     *       startTime = now()
     *       
     *       try:
     *           # Calculate degradation delay
     *           overLimit = max(0, concurrent - request.softLimit)
     *           delayMs = overLimit * request.degradationFactor
     *           
     *           # Apply delay in chunks, checking for timeout exception
     *           remainingDelay = delayMs
     *           while remainingDelay > 0:
     *               sleepTime = min(remainingDelay, 1000)
     *               sleep(sleepTime)
     *               remainingDelay -= sleepTime
     *               
     *               elapsed = (now() - startTime) in seconds
     *               if elapsed > 120 and random() < 0.20:
     *                   throw randomException()
     *           
     *           # Perform CPU work
     *           performHashIterations(request.workIterations)
     *           
     *           # Allocate and use memory (auto-released on function exit)
     *           buffer = allocateBytes(request.bufferSizeKb * 1024)
     *           touchMemory(buffer)  # Prevent optimization from skipping
     *           
     *           # Final timeout check
     *           elapsed = (now() - startTime) in seconds
     *           if elapsed > 120 and random() < 0.20:
     *               throw randomException()
     *           
     *           return result(elapsed, concurrent, delayMs, success)
     *           
     *       finally:
     *           atomicDecrement(concurrentCounter)
     *           atomicIncrement(totalRequestsCounter)
     *           atomicAdd(totalResponseTime, elapsed)
     */

    /// <inheritdoc />
    public async Task<LoadTestResult> ExecuteWorkAsync(LoadTestRequest request, CancellationToken cancellationToken)
    {
        /*
         * =====================================================================
         * REDESIGNED ALGORITHM: Sustained Resource Consumption
         * =====================================================================
         * 
         * PROBLEM WITH PREVIOUS DESIGN:
         * - CPU work was brief (~5ms) then sleep → CPU spikes didn't accumulate
         * - Memory was allocated at end and immediately released
         * - Result: Either 0% CPU (sleep) or 100% CPU (spin-wait), nothing in between
         * 
         * NEW DESIGN:
         * - Allocate memory FIRST and hold for entire request duration
         * - Calculate total work duration based on concurrent requests
         * - Loop throughout duration doing INTERLEAVED CPU work and brief sleeps
         * - This creates SUSTAINED load where CPU%, Memory, and Thread Pool all scale
         * 
         * PSEUDOCODE:
         *   concurrent = atomicIncrement(counter)
         *   buffer = allocateMemory(bufferSizeKb)  # Hold for entire request
         *   
         *   totalDurationMs = baselineDelayMs + degradationDelay(concurrent)
         *   cpuWorkPerCycleMs = min(50, workIterations / 100)  # ~50ms CPU per cycle
         *   sleepPerCycleMs = 50  # 50ms sleep per cycle
         *   
         *   while elapsed < totalDurationMs:
         *       doCpuWork(cpuWorkPerCycleMs worth of iterations)
         *       touchMemory(buffer)  # Keep memory active
         *       sleep(sleepPerCycleMs)
         *       checkTimeout()
         *   
         *   return result
         *   # buffer released here - memory held for ENTIRE request duration
         */
        
        var currentConcurrent = Interlocked.Increment(ref _concurrentRequests);
        UpdatePeakConcurrent(currentConcurrent);
        
        var stopwatch = Stopwatch.StartNew();
        var totalCpuWorkDone = 0;
        var workCompleted = false;
        byte[]? buffer = null;

        try
        {
            /*
             * STEP 1: ALLOCATE MEMORY UP FRONT
             * =================================================================
             * Allocate memory at the START and hold it for the entire request.
             * This ensures memory scales with concurrent requests.
             */
            var bufferSize = request.BufferSizeKb * 1024;
            buffer = new byte[bufferSize];
            
            // Touch memory immediately to ensure actual allocation
            TouchMemoryBuffer(buffer);
            
            /*
             * STEP 2: CALCULATE TOTAL REQUEST DURATION
             * =================================================================
             * 
             * Formula: baselineDelayMs + (overLimit * degradationFactor)
             * 
             * This determines how long the request will hold resources.
             */
            var overLimit = Math.Max(0, currentConcurrent - request.SoftLimit);
            var degradationDelayMs = overLimit * request.DegradationFactor;
            var totalDurationMs = request.BaselineDelayMs + degradationDelayMs;
            
            _logger.LogDebug(
                "Load test: Concurrent={Concurrent}, Duration={Duration}ms (base={Base}ms + degradation={Degradation}ms)",
                currentConcurrent, totalDurationMs, request.BaselineDelayMs, degradationDelayMs);
            
            /*
             * STEP 3: SUSTAINED WORK LOOP
             * =================================================================
             * 
             * Instead of doing all CPU work at once then sleeping, we interleave:
             * - Do ~50ms worth of CPU work (hash iterations)
             * - Touch the memory buffer (keeps it active, prevents GC optimization)
             * - Sleep briefly (~50ms)
             * - Repeat until total duration reached
             * 
             * This creates ~50% CPU utilization per active thread, allowing
             * CPU to scale with concurrency without immediately hitting 100%.
             * 
             * TUNING:
             * - workIterations controls CPU intensity per cycle
             * - Higher workIterations = more CPU per cycle
             * - 0 workIterations = pure thread blocking (minimal CPU)
             */
            const int CycleMs = 100;  // Each cycle is ~100ms (50ms work + 50ms sleep)
            const int SleepPerCycleMs = 50;
            
            // Calculate iterations per cycle to spread workIterations across duration
            var totalCycles = Math.Max(1, totalDurationMs / CycleMs);
            var iterationsPerCycle = request.WorkIterations > 0 
                ? Math.Max(100, request.WorkIterations / totalCycles)
                : 0;
            
            while (stopwatch.ElapsedMilliseconds < totalDurationMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // CPU work phase
                if (iterationsPerCycle > 0)
                {
                    PerformCpuWork(iterationsPerCycle);
                    totalCpuWorkDone += iterationsPerCycle;
                }
                
                // Keep memory active (prevents GC from collecting early)
                TouchMemoryBuffer(buffer);
                
                // Check for timeout exception
                CheckAndThrowTimeoutException(stopwatch);
                
                // Sleep phase (allows other threads to run, prevents 100% CPU)
                var remainingMs = totalDurationMs - (int)stopwatch.ElapsedMilliseconds;
                var sleepMs = Math.Min(SleepPerCycleMs, Math.Max(0, remainingMs));
                if (sleepMs > 0)
                {
                    Thread.Sleep(sleepMs);
                }
            }
            
            // Final memory touch before returning
            TouchMemoryBuffer(buffer);
            
            workCompleted = true;
            stopwatch.Stop();
            
            return BuildResult(
                stopwatch.ElapsedMilliseconds,
                currentConcurrent,
                (int)(stopwatch.ElapsedMilliseconds),  // Total time blocked
                totalCpuWorkDone,
                buffer.Length,
                workCompleted,
                exceptionThrown: false,
                exceptionType: null);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _totalExceptionsThrown);
            Interlocked.Increment(ref _periodExceptions);
            
            _logger.LogWarning(
                ex,
                "Load test exception after {Elapsed}ms: {ExceptionType}",
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name);
            
            throw;
        }
        finally
        {
            // Memory (buffer) is released here when method exits
            // Counter updates
            Interlocked.Decrement(ref _concurrentRequests);
            Interlocked.Increment(ref _totalRequestsProcessed);
            Interlocked.Add(ref _totalResponseTimeMs, stopwatch.ElapsedMilliseconds);
            
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            Interlocked.Increment(ref _periodRequestsCompleted);
            Interlocked.Add(ref _periodResponseTimeSum, elapsedMs);
            UpdateMaxResponseTime(elapsedMs);
        }
    }
    
    /// <summary>
    /// Touches all bytes in the memory buffer to keep it active.
    /// This prevents the GC from collecting the memory early and ensures
    /// it shows up in memory metrics.
    /// </summary>
    private void TouchMemoryBuffer(byte[] buffer)
    {
        // XOR through buffer to ensure memory is actually accessed
        // This is fast but prevents optimization
        var checksum = 0;
        for (var i = 0; i < buffer.Length; i += 4096) // Touch every page
        {
            buffer[i] = (byte)(buffer[i] ^ 0xFF);
            checksum += buffer[i];
        }
        // Use checksum to prevent optimization
        if (checksum < -1000000) _logger.LogTrace("Checksum: {Checksum}", checksum);
    }

    /*
     * =========================================================================
     * HELPER: Check and Throw Timeout Exception
     * =========================================================================
     * 
     * ALGORITHM:
     *   if elapsed_seconds > 120:
     *       if random() < 0.20:
     *           throw random_exception_from_pool()
     * 
     * The 20% probability creates realistic sporadic failures.
     */
    
    /// <summary>
    /// Checks if elapsed time exceeds threshold and randomly throws an exception.
    /// </summary>
    /// <param name="stopwatch">Stopwatch tracking request duration.</param>
    private void CheckAndThrowTimeoutException(Stopwatch stopwatch)
    {
        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        
        if (elapsedSeconds > ExceptionThresholdSeconds)
        {
            /*
             * RANDOM NUMBER GENERATION
             * =================================================================
             * 
             * _random.NextDouble() returns value between 0.0 and 1.0
             * If value < 0.20, we throw (20% chance)
             * 
             * PORTING:
             * - PHP: if (mt_rand() / mt_getrandmax() < 0.20)
             * - Node.js: if (Math.random() < 0.20)
             * - Java: if (random.nextDouble() < 0.20)
             * - Python: if random.random() < 0.20
             */
            if (_random.NextDouble() < ExceptionProbability)
            {
                // Pick random exception from pool
                var exceptionIndex = _random.Next(ExceptionFactories.Length);
                var exception = ExceptionFactories[exceptionIndex]();
                
                _logger.LogInformation(
                    "Load test throwing random exception after {Elapsed}s: {ExceptionType}",
                    elapsedSeconds,
                    exception.GetType().Name);
                
                throw exception;
            }
        }
    }

    /*
     * =========================================================================
     * HELPER: Perform CPU Work
     * =========================================================================
     * 
     * ALGORITHM:
     *   data = "LoadTest-" + randomBytes
     *   for i in range(iterations):
     *       hash = SHA256(data)
     *       data = hash  # Feed output back as input
     * 
     * This creates consistent, non-optimizable CPU work.
     */
    
    /// <summary>
    /// Performs CPU-intensive work by computing SHA256 hashes.
    /// </summary>
    /// <param name="iterations">Number of hash iterations to perform.</param>
    private void PerformCpuWork(int iterations)
    {
        /*
         * IMPLEMENTATION NOTES:
         * 
         * We start with a seed value and repeatedly hash it.
         * Each hash output becomes the input for the next iteration.
         * This prevents the compiler from optimizing away the work.
         * 
         * SHA256 CHOICE:
         * - Consistent performance across platforms
         * - Available in all languages' standard libraries
         * - Sufficient CPU load without being excessive
         * 
         * PORTING:
         * Replace SHA256 with your language's equivalent:
         * - PHP: hash('sha256', $data, true)
         * - Node.js: crypto.createHash('sha256').update(data).digest()
         * - Java: MessageDigest.getInstance("SHA-256").digest(data)
         * - Python: hashlib.sha256(data).digest()
         */
        using var sha256 = SHA256.Create();
        var data = Encoding.UTF8.GetBytes($"LoadTest-{Guid.NewGuid()}");
        
        for (var i = 0; i < iterations; i++)
        {
            data = sha256.ComputeHash(data);
        }
        
        // Use the result to prevent optimization
        // (compiler can't remove work if result is used)
        _ = data.Length;
    }
    /*
     * =========================================================================
     * HELPER: Build Result
     * =========================================================================
     */
    
    /// <summary>
    /// Builds the load test result object.
    /// </summary>
    private LoadTestResult BuildResult(
        long elapsedMs,
        int concurrentRequests,
        int degradationDelayMs,
        int workIterations,
        int bufferSizeBytes,
        bool workCompleted,
        bool exceptionThrown,
        string? exceptionType)
    {
        return new LoadTestResult
        {
            ElapsedMs = elapsedMs,
            ConcurrentRequestsAtStart = concurrentRequests,
            DegradationDelayAppliedMs = degradationDelayMs,
            WorkIterationsCompleted = workCompleted ? workIterations : 0,
            MemoryAllocatedBytes = workCompleted ? bufferSizeBytes : 0,
            WorkCompleted = workCompleted,
            ExceptionThrown = exceptionThrown,
            ExceptionType = exceptionType,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /*
     * =========================================================================
     * STATISTICS METHOD
     * =========================================================================
     */

    /// <inheritdoc />
    public LoadTestStats GetCurrentStats()
    {
        /*
         * ATOMIC READS
         * =================================================================
         * 
         * For 32-bit integers on 64-bit systems, reads are atomic.
         * For 64-bit longs, we use Interlocked.Read for atomic access.
         * 
         * Average calculation uses current values (may have slight race
         * conditions but that's acceptable for monitoring stats).
         */
        var totalRequests = Interlocked.Read(ref _totalRequestsProcessed);
        var totalTime = Interlocked.Read(ref _totalResponseTimeMs);
        var avgResponseTime = totalRequests > 0 ? (double)totalTime / totalRequests : 0;
        
        return new LoadTestStats(
            CurrentConcurrentRequests: _concurrentRequests,
            TotalRequestsProcessed: totalRequests,
            TotalExceptionsThrown: Interlocked.Read(ref _totalExceptionsThrown),
            AverageResponseTimeMs: avgResponseTime
        );
    }
    
    /*
     * =========================================================================
     * PERIOD STATS HELPER METHODS
     * =========================================================================
     * 
     * These methods track statistics for the current 60-second reporting period.
     * They use compare-and-swap (CAS) loops for thread-safe max tracking.
     */
    
    /// <summary>
    /// Thread-safe update of peak concurrent requests for the current period.
    /// Uses compare-and-swap pattern for atomic max tracking.
    /// </summary>
    private void UpdatePeakConcurrent(int currentConcurrent)
    {
        int currentPeak;
        do
        {
            currentPeak = _periodPeakConcurrent;
            if (currentConcurrent <= currentPeak)
            {
                return; // Current peak is already higher
            }
        }
        while (Interlocked.CompareExchange(ref _periodPeakConcurrent, currentConcurrent, currentPeak) != currentPeak);
    }
    
    /// <summary>
    /// Thread-safe update of max response time for the current period.
    /// Uses compare-and-swap pattern for atomic max tracking.
    /// </summary>
    private void UpdateMaxResponseTime(long responseTimeMs)
    {
        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _periodMaxResponseTimeMs);
            if (responseTimeMs <= currentMax)
            {
                return; // Current max is already higher
            }
        }
        while (Interlocked.CompareExchange(ref _periodMaxResponseTimeMs, responseTimeMs, currentMax) != currentMax);
    }
}

/*
 * =============================================================================
 * COMPLETE FILE LISTING FOR AI PORTING
 * =============================================================================
 * 
 * To port this feature to another language, you need these files:
 * 
 * 1. Controllers/LoadTestController.cs
 *    - HTTP routing and request handling
 *    - Delegates to service layer
 * 
 * 2. Services/ILoadTestService.cs
 *    - Interface definition
 *    - LoadTestStats record
 * 
 * 3. Services/LoadTestService.cs (THIS FILE)
 *    - Core algorithm implementation
 *    - Thread-safe counters
 *    - Random exception pool
 *    - CPU and memory work methods
 * 
 * 4. Models/LoadTestRequest.cs
 *    - Input parameters with defaults
 * 
 * 5. Models/LoadTestResult.cs
 *    - Output format
 * 
 * 6. Program.cs (modification)
 *    - Service registration: builder.Services.AddSingleton<ILoadTestService, LoadTestService>();
 * 
 * KEY ALGORITHMS TO PORT:
 * 
 * 1. Degradation Delay:
 *    delay = max(0, concurrent - softLimit) * degradationFactor
 * 
 * 2. Exception Probability:
 *    if elapsedSeconds > 120 and random() < 0.20: throw randomException()
 * 
 * 3. Thread-Safe Counter:
 *    Use atomic increment/decrement for concurrent request tracking
 * 
 * 4. CPU Work:
 *    Repeated SHA256 hashing with output fed back as input
 * 
 * 5. Memory Allocation:
 *    Allocate buffer and write pattern to force actual allocation
 * 
 * =============================================================================
 */
