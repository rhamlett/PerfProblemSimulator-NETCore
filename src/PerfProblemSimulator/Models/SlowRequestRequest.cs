namespace PerfProblemSimulator.Models;

/// <summary>
/// Configuration for slow request simulation that causes thread pool starvation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Request body for POST /api/slowrequest/start. Controls how quickly thread pool
/// starvation occurs and how long it takes for CLR Profiler to capture useful data.
/// </para>
/// <para>
/// <strong>OPTIMAL VALUES FOR CLR PROFILER TRAINING:</strong>
/// <list type="bullet">
/// <item>RequestDurationSeconds = 25: Long enough to block thread, short enough to complete in 60s capture</item>
/// <item>IntervalSeconds = 10: Creates 6 concurrent requests in 60 seconds (enough to starve)</item>
/// <item>MaxRequests = 6: Stops after capture window (prevents indefinite resource consumption)</item>
/// </list>
/// </para>
/// <para>
/// <strong>THREAD STARVATION MATH:</strong>
/// With default settings (25s duration, 10s interval), after 60 seconds you'll have:
/// - Request 1: Started at 0s, blocking until 25s
/// - Request 2: Started at 10s, blocking until 35s
/// - Request 3: Started at 20s, blocking until 45s
/// - etc.
/// By ~50 seconds, 5-6 threads are blocked simultaneously, causing visible starvation.
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// Same parameters apply regardless of language. Adjust duration based on thread pool size:
/// larger pools need more concurrent requests or longer durations to exhaust.
/// </para>
/// </remarks>
public class SlowRequestRequest
{
    /// <summary>
    /// Approximate duration for each slow request in seconds.
    /// Default: 25 seconds (ideal for 60-second CLR Profile capture).
    /// </summary>
    public int RequestDurationSeconds { get; set; } = 25;

    /// <summary>
    /// Interval between spawning new slow requests in seconds.
    /// Default: 10 seconds.
    /// </summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of requests to send. 0 = unlimited until stopped.
    /// Default: 0 (unlimited).
    /// </summary>
    public int MaxRequests { get; set; } = 0;
}

/// <summary>
/// The type of slow request scenario to simulate.
/// </summary>
public enum SlowRequestScenario
{
    /// <summary>
    /// Randomly selects from all available scenarios.
    /// </summary>
    Random,

    /// <summary>
    /// Simple sync-over-async with .Result and .Wait() calls.
    /// Profiler shows: Time blocked at Task.Result and Task.Wait().
    /// </summary>
    SimpleSyncOverAsync,

    /// <summary>
    /// Nested sync-over-async with multiple layers of blocking.
    /// Profiler shows: Chain of blocking calls through multiple methods.
    /// </summary>
    NestedSyncOverAsync,

    /// <summary>
    /// Realistic database/HTTP pattern with GetAwaiter().GetResult().
    /// Profiler shows: Common pattern found in legacy code migrations.
    /// </summary>
    DatabasePattern
}
