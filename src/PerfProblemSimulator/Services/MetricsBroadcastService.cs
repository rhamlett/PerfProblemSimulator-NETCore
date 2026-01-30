using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

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
/// <strong>Why a separate service?</strong>
/// </para>
/// <list type="bullet">
/// <item>MetricsCollector is focused on data collection (single responsibility)</item>
/// <item>SignalR hub context can be injected here safely</item>
/// <item>Allows metrics collection to work even with no connected clients</item>
/// <item>Graceful startup/shutdown via IHostedService lifecycle</item>
/// </list>
/// </remarks>
public class MetricsBroadcastService : IHostedService
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ISimulationTracker _simulationTracker;
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly ILogger<MetricsBroadcastService> _logger;

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
        _metricsCollector.MetricsCollected += OnMetricsCollected;
        _simulationTracker.SimulationStarted += OnSimulationStarted;
        _simulationTracker.SimulationCompleted += OnSimulationCompleted;
        _metricsCollector.Start();

        _logger.LogInformation("Metrics broadcast service started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _metricsCollector.MetricsCollected -= OnMetricsCollected;
        _simulationTracker.SimulationStarted -= OnSimulationStarted;
        _simulationTracker.SimulationCompleted -= OnSimulationCompleted;
        _metricsCollector.Stop();

        _logger.LogInformation("Metrics broadcast service stopped");
        return Task.CompletedTask;
    }

    private async void OnMetricsCollected(object? sender, MetricsSnapshot snapshot)
    {
        try
        {
            await _hubContext.Clients.All.ReceiveMetrics(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting metrics to clients");
        }
    }

    private async void OnSimulationStarted(object? sender, SimulationEventArgs e)
    {
        try
        {
            await _hubContext.Clients.All.SimulationStarted(e.Type.ToString(), e.SimulationId);
            _logger.LogDebug("Broadcast SimulationStarted: {Type} {Id}", e.Type, e.SimulationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting simulation started event");
        }
    }

    private async void OnSimulationCompleted(object? sender, SimulationEventArgs e)
    {
        try
        {
            await _hubContext.Clients.All.SimulationCompleted(e.Type.ToString(), e.SimulationId);
            _logger.LogDebug("Broadcast SimulationCompleted: {Type} {Id}", e.Type, e.SimulationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting simulation completed event");
        }
    }
}
