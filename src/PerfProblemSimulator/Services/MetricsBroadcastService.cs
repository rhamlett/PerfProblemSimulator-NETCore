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
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly ILogger<MetricsBroadcastService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsBroadcastService"/> class.
    /// </summary>
    public MetricsBroadcastService(
        IMetricsCollector metricsCollector,
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        ILogger<MetricsBroadcastService> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _metricsCollector.MetricsCollected += OnMetricsCollected;
        _metricsCollector.Start();

        _logger.LogInformation("Metrics broadcast service started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _metricsCollector.MetricsCollected -= OnMetricsCollected;
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
}
