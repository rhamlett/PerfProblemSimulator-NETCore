using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Hubs;

/// <summary>
/// WebSocket/SignalR hub for real-time metrics broadcasting to dashboard clients.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Provides a persistent bidirectional communication channel between the server and
/// connected browser clients. This enables real-time dashboard updates without polling.
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>Browser connects to /hubs/metrics endpoint (WebSocket preferred, with fallbacks)</item>
/// <item>On connect: immediately send current metrics so client doesn't wait for next tick</item>
/// <item>Every 1 second: MetricsBroadcastService pushes metrics to all connected clients</item>
/// <item>On simulation events: push notifications so UI can update status indicators</item>
/// <item>On disconnect: clean up resources (automatic via framework)</item>
/// </list>
/// </para>
/// <para>
/// <strong>WHY REAL-TIME vs POLLING:</strong>
/// <list type="bullet">
/// <item>Sub-second updates are essential for visualizing performance problems as they happen</item>
/// <item>Push-based is more efficient than clients polling every second</item>
/// <item>Connection state enables cleanup when browsers close</item>
/// <item>Transport fallback (WebSocket → SSE → Long Polling) ensures broad compatibility</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: Use Ratchet or ReactPHP for WebSocket server, or external service like Pusher</item>
/// <item>Node.js: Use Socket.IO - nearly identical concept with io.on('connection', socket => ...)</item>
/// <item>Java/Spring: Use @MessageMapping with SimpMessagingTemplate for WebSocket STOMP</item>
/// <item>Python: Use Flask-SocketIO with @socketio.on('connect') handlers</item>
/// <item>Ruby: Use ActionCable with channel subscriptions</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// <list type="bullet">
/// <item>Hubs/IMetricsClient.cs - Message contract (what can be sent to clients)</item>
/// <item>Services/MetricsBroadcastService.cs - Background service that triggers broadcasts</item>
/// <item>wwwroot/js/dashboard.js - JavaScript SignalR client connection</item>
/// <item>Program.cs - Hub endpoint mapping (/hubs/metrics)</item>
/// </list>
/// </para>
/// </remarks>
public class MetricsHub : Hub<IMetricsClient>
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly IIdleStateService _idleStateService;
    private readonly ILogger<MetricsHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsHub"/> class.
    /// </summary>
    public MetricsHub(
        IMetricsCollector metricsCollector,
        IIdleStateService idleStateService,
        ILogger<MetricsHub> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _idleStateService = idleStateService ?? throw new ArgumentNullException(nameof(idleStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);

        // NOTE: We do NOT auto-wake here. The client explicitly calls WakeUp() on page load.
        // This prevents SignalR auto-reconnects from waking the app unexpectedly.
        // Only intentional page loads should wake the app from idle state.

        // Send current metrics immediately so client doesn't have to wait
        var currentSnapshot = _metricsCollector.LatestSnapshot;
        await Clients.Caller.ReceiveMetrics(currentSnapshot);

        // NOTE: We intentionally do NOT send idle state here.
        // The client calls WakeUp() immediately after connecting, which will
        // wake the app if needed and send the correct idle state.
        // Sending IsIdle=true here would cause the client to disconnect
        // before WakeUp() can be invoked (race condition).

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
    [UsedImplicitly]
    public async Task RequestMetrics()
    {
        // Record activity to prevent idle timeout
        _idleStateService.RecordActivity();
        
        var snapshot = _metricsCollector.LatestSnapshot;
        await Clients.Caller.ReceiveMetrics(snapshot);
    }

    /// <summary>
    /// Client calls this to wake up the server from idle state.
    /// Used when the dashboard page loads or user interacts with it.
    /// </summary>
    [UsedImplicitly]
    public async Task WakeUp()
    {
        var wasIdle = _idleStateService.WakeUp();
        
        if (wasIdle)
        {
            _logger.LogInformation("Server woken up by client request from: {ConnectionId}", Context.ConnectionId);
        }

        // Always send current idle state directly to the caller.
        // When waking from idle, the broadcast via MetricsBroadcastService may be
        // delayed (queued on the dedicated broadcast thread), so we must send
        // the updated state directly to ensure the client knows we're active.
        var idleData = new IdleStateData
        {
            IsIdle = false,
            Message = wasIdle
                ? "App waking up from idle state. There may be gaps in diagnostics and logs."
                : "Application is active",
            Timestamp = DateTimeOffset.UtcNow
        };
        await Clients.Caller.ReceiveIdleState(idleData);
    }

    /// <summary>
    /// Returns the server's current idle state without waking it.
    /// Called by the client after auto-reconnect to determine whether the
    /// server is idle (and therefore the client should disconnect again)
    /// or active (and the client should stay connected).
    /// </summary>
    [UsedImplicitly]
    public IdleStateData GetIdleState()
    {
        var isIdle = _idleStateService.IsIdle;
        return new IdleStateData
        {
            IsIdle = isIdle,
            Message = isIdle
                ? "Application is idle, no health probes being sent."
                : "Application is active",
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
