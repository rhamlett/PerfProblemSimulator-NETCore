using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Hubs;

/// <summary>
/// Interface for SignalR metrics client methods.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This interface defines the methods that can be called on connected clients.
/// SignalR uses this for strongly-typed hub methods, providing compile-time
/// safety instead of magic strings.
/// </para>
/// <para>
/// When the server calls <c>Clients.All.ReceiveMetrics(snapshot)</c>, SignalR
/// automatically serializes the snapshot and sends it to all connected browsers
/// via WebSockets (or fallback transports).
/// </para>
/// </remarks>
public interface IMetricsClient
{
    /// <summary>
    /// Receives a metrics snapshot update.
    /// </summary>
    /// <param name="snapshot">The latest metrics snapshot.</param>
    Task ReceiveMetrics(MetricsSnapshot snapshot);

    /// <summary>
    /// Receives a simulation started notification.
    /// </summary>
    /// <param name="simulationType">Type of simulation that started.</param>
    /// <param name="simulationId">ID of the simulation.</param>
    Task SimulationStarted(string simulationType, Guid simulationId);

    /// <summary>
    /// Receives a simulation completed notification.
    /// </summary>
    /// <param name="simulationType">Type of simulation that completed.</param>
    /// <param name="simulationId">ID of the simulation.</param>
    Task SimulationCompleted(string simulationType, Guid simulationId);

    /// <summary>
    /// Receives a latency measurement from the server-side probe.
    /// </summary>
    /// <param name="measurement">The latency measurement data.</param>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> This measurement shows real request processing
    /// latency. Compare baseline latency (~5-20ms) with latency during thread pool
    /// starvation (can exceed 30 seconds!) to see the impact of blocking threads.
    /// </para>
    /// </remarks>
    Task ReceiveLatency(LatencyMeasurement measurement);

    /// <summary>
    /// Receives slow request latency data from the server.
    /// </summary>
    /// <param name="data">The slow request latency data.</param>
    /// <remarks>
    /// <para>
    /// This is used to track actual slow request durations (typically 20-25 seconds)
    /// separately from the lightweight probe latency.
    /// </para>
    /// </remarks>
    Task ReceiveSlowRequestLatency(SlowRequestLatencyData data);

    /// <summary>
    /// Receives load test statistics update for event log display.
    /// </summary>
    /// <param name="data">The load test statistics data.</param>
    /// <remarks>
    /// <para>
    /// This is broadcast every 60 seconds while the load test endpoint is receiving
    /// traffic. Shows concurrent requests, average response time, and throughput.
    /// </para>
    /// </remarks>
    Task ReceiveLoadTestStats(LoadTestStatsData data);
}

/// <summary>
/// Data about a slow request's latency.
/// </summary>
public class SlowRequestLatencyData
{
    /// <summary>
    /// The request number in the simulation.
    /// </summary>
    public int RequestNumber { get; set; }
    
    /// <summary>
    /// The scenario used for this request.
    /// </summary>
    public string Scenario { get; set; } = "";
    
    /// <summary>
    /// The measured latency in milliseconds.
    /// </summary>
    public double LatencyMs { get; set; }
    
    /// <summary>
    /// When this measurement was taken.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The expected duration of the request in milliseconds (Processing Time).
    /// </summary>
    public double ExpectedDurationMs { get; set; }

    /// <summary>
    /// Whether the request failed or timed out.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Error message if the request failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Data about load test endpoint statistics for event log display.
/// Broadcast every 60 seconds while the endpoint is receiving traffic.
/// </summary>
public class LoadTestStatsData
{
    /// <summary>
    /// Current number of concurrent requests being processed.
    /// </summary>
    public int CurrentConcurrent { get; set; }
    
    /// <summary>
    /// Peak concurrent requests observed in this reporting period.
    /// </summary>
    public int PeakConcurrent { get; set; }
    
    /// <summary>
    /// Total requests completed in this reporting period.
    /// </summary>
    public long RequestsCompleted { get; set; }
    
    /// <summary>
    /// Average response time in milliseconds for this period.
    /// </summary>
    public double AvgResponseTimeMs { get; set; }
    
    /// <summary>
    /// Maximum response time observed in this period.
    /// </summary>
    public double MaxResponseTimeMs { get; set; }
    
    /// <summary>
    /// Requests per second throughput.
    /// </summary>
    public double RequestsPerSecond { get; set; }
    
    /// <summary>
    /// Number of exceptions thrown (after 120s of traffic).
    /// </summary>
    public int ExceptionCount { get; set; }
    
    /// <summary>
    /// When this stats snapshot was taken.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}
