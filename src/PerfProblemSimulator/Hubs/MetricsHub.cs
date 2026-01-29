using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Hubs;

/// <summary>
/// SignalR hub for real-time metrics broadcasting to dashboard clients.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// SignalR provides real-time communication between server and browser clients.
/// This hub pushes metrics to all connected dashboard instances automatically.
/// </para>
/// <para>
/// <strong>How it works:</strong>
/// </para>
/// <list type="number">
/// <item>Browser connects to /hubs/metrics via WebSocket (with fallback)</item>
/// <item>MetricsCollector fires MetricsCollected event every second</item>
/// <item>This hub broadcasts the snapshot to all connected clients</item>
/// <item>Browser JavaScript receives the data and updates charts</item>
/// </list>
/// <para>
/// <strong>Why SignalR over polling?</strong>
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Efficiency</term>
/// <description>Single connection, push-based updates</description>
/// </item>
/// <item>
/// <term>Real-time</term>
/// <description>Sub-second latency vs polling intervals</description>
/// </item>
/// <item>
/// <term>Scalability</term>
/// <description>SignalR handles connection management</description>
/// </item>
/// <item>
/// <term>Transport flexibility</term>
/// <description>WebSocket → Server-Sent Events → Long Polling fallback</description>
/// </item>
/// </list>
/// </remarks>
public class MetricsHub : Hub<IMetricsClient>
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<MetricsHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsHub"/> class.
    /// </summary>
    public MetricsHub(IMetricsCollector metricsCollector, ILogger<MetricsHub> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);

        // Send current metrics immediately so client doesn't have to wait
        var currentSnapshot = _metricsCollector.LatestSnapshot;
        await Clients.Caller.ReceiveMetrics(currentSnapshot);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "Dashboard client disconnected with error: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("Dashboard client disconnected: {ConnectionId}", Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client can request the latest metrics snapshot on demand.
    /// </summary>
    public async Task RequestMetrics()
    {
        var snapshot = _metricsCollector.LatestSnapshot;
        await Clients.Caller.ReceiveMetrics(snapshot);
    }
}
