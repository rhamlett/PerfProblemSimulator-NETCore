using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;
using System.Collections.Concurrent;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Background service that broadcasts metrics to SignalR clients.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This hosted service acts as a bridge between the MetricsCollector and SignalR.
/// It subscribes to metrics events and broadcasts them to all connected clients.
/// </para>
/// <para>
/// <strong>Thread Pool Independence (Critical for Load Testing):</strong>
/// </para>
/// <para>
/// When the load test endpoint exhausts the thread pool, SignalR broadcasts would
/// normally freeze because they rely on thread pool threads. To prevent this, we use:
/// </para>
/// <list type="bullet">
/// <item>A dedicated broadcast thread (not from thread pool)</item>
/// <item>A message queue (BlockingCollection) for thread-safe message passing</item>
/// <item>Fire-and-forget semantics that don't await thread pool continuations</item>
/// </list>
/// <para>
/// This ensures the dashboard continues updating even during severe thread pool starvation.
/// </para>
/// </remarks>
public class MetricsBroadcastService : IHostedService
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ISimulationTracker _simulationTracker;
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly ILogger<MetricsBroadcastService> _logger;
    
    // Message queue for thread-pool-independent broadcasting
    private readonly BlockingCollection<BroadcastMessage> _messageQueue = new(boundedCapacity: 100);
    private Thread? _broadcastThread;
    private volatile bool _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsBroadcastService"/> class.
    /// </summary>
    public MetricsBroadcastService(
        IMetricsCollector metricsCollector,
        ISimulationTracker simulationTracker,
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        ILogger<MetricsBroadcastService> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _running = true;
        
        // Start dedicated broadcast thread (not from thread pool)
        _broadcastThread = new Thread(BroadcastLoop)
        {
            Name = "SignalR-Broadcast",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal // Prioritize dashboard updates
        };
        _broadcastThread.Start();
        
        _metricsCollector.MetricsCollected += OnMetricsCollected;
        _simulationTracker.SimulationStarted += OnSimulationStarted;
        _simulationTracker.SimulationCompleted += OnSimulationCompleted;
        _metricsCollector.Start();

        _logger.LogInformation("Metrics broadcast service started with dedicated broadcast thread");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _running = false;
        _messageQueue.CompleteAdding();
        
        _metricsCollector.MetricsCollected -= OnMetricsCollected;
        _simulationTracker.SimulationStarted -= OnSimulationStarted;
        _simulationTracker.SimulationCompleted -= OnSimulationCompleted;
        _metricsCollector.Stop();
        
        // Wait for broadcast thread to finish (with timeout)
        _broadcastThread?.Join(TimeSpan.FromSeconds(5));

        _logger.LogInformation("Metrics broadcast service stopped");
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Dedicated thread loop that processes broadcast messages.
    /// This runs independently of the thread pool.
    /// </summary>
    private void BroadcastLoop()
    {
        _logger.LogDebug("Broadcast thread started");
        
        while (_running || _messageQueue.Count > 0)
        {
            try
            {
                // TryTake with timeout to allow checking _running flag
                if (_messageQueue.TryTake(out var message, TimeSpan.FromMilliseconds(100)))
                {
                    ProcessMessage(message);
                }
            }
            catch (InvalidOperationException)
            {
                // Collection was marked as complete - exit loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in broadcast loop");
            }
        }
        
        _logger.LogDebug("Broadcast thread exiting");
    }
    
    /// <summary>
    /// Process a single broadcast message.
    /// Uses GetAwaiter().GetResult() to avoid thread pool dependency.
    /// </summary>
    private void ProcessMessage(BroadcastMessage message)
    {
        try
        {
            switch (message.Type)
            {
                case BroadcastType.Metrics:
                    _hubContext.Clients.All.ReceiveMetrics((MetricsSnapshot)message.Data!).GetAwaiter().GetResult();
                    break;
                    
                case BroadcastType.SimulationStarted:
                    var startArgs = (SimulationEventArgs)message.Data!;
                    _hubContext.Clients.All.SimulationStarted(startArgs.Type.ToString(), startArgs.SimulationId).GetAwaiter().GetResult();
                    _logger.LogDebug("Broadcast SimulationStarted: {Type} {Id}", startArgs.Type, startArgs.SimulationId);
                    break;
                    
                case BroadcastType.SimulationCompleted:
                    var completeArgs = (SimulationEventArgs)message.Data!;
                    _hubContext.Clients.All.SimulationCompleted(completeArgs.Type.ToString(), completeArgs.SimulationId).GetAwaiter().GetResult();
                    _logger.LogDebug("Broadcast SimulationCompleted: {Type} {Id}", completeArgs.Type, completeArgs.SimulationId);
                    break;
                    
                case BroadcastType.Latency:
                    _hubContext.Clients.All.ReceiveLatency((LatencyMeasurement)message.Data!).GetAwaiter().GetResult();
                    break;
                    
                case BroadcastType.SlowRequestLatency:
                    _hubContext.Clients.All.ReceiveSlowRequestLatency((SlowRequestLatencyData)message.Data!).GetAwaiter().GetResult();
                    break;
                    
                case BroadcastType.LoadTestStats:
                    _hubContext.Clients.All.ReceiveLoadTestStats((LoadTestStatsData)message.Data!).GetAwaiter().GetResult();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting {Type} message", message.Type);
        }
    }

    private void OnMetricsCollected(object? sender, MetricsSnapshot snapshot)
    {
        // Queue message - don't block if queue is full (drop oldest metrics)
        if (!_messageQueue.TryAdd(new BroadcastMessage(BroadcastType.Metrics, snapshot)))
        {
            _logger.LogTrace("Broadcast queue full, dropping metrics update");
        }
    }

    private void OnSimulationStarted(object? sender, SimulationEventArgs e)
    {
        _messageQueue.TryAdd(new BroadcastMessage(BroadcastType.SimulationStarted, e));
    }

    private void OnSimulationCompleted(object? sender, SimulationEventArgs e)
    {
        _messageQueue.TryAdd(new BroadcastMessage(BroadcastType.SimulationCompleted, e));
    }
    
    /// <summary>
    /// Message types for the broadcast queue.
    /// </summary>
    private enum BroadcastType
    {
        Metrics,
        SimulationStarted,
        SimulationCompleted,
        Latency,
        SlowRequestLatency,
        LoadTestStats
    }
    
    /// <summary>
    /// Wrapper for broadcast messages in the queue.
    /// </summary>
    private record BroadcastMessage(BroadcastType Type, object? Data);
}
