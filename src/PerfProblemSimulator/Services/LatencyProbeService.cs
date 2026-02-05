using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using PerfProblemSimulator.Hubs;
using System.Diagnostics;
using System.Net.Http;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Background service that measures request latency by probing an internal endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This service demonstrates how thread pool starvation
/// affects request processing latency. It runs on a dedicated thread (not the thread pool)
/// to ensure it can always measure latency, even during severe starvation conditions.
/// </para>
/// <para>
/// <strong>Why a Dedicated Thread?</strong>
/// </para>
/// <para>
/// The thread pool is shared by:
/// <list type="bullet">
/// <item>Incoming HTTP request processing</item>
/// <item>Task continuations (async/await)</item>
/// <item>Timer callbacks</item>
/// <item>ThreadPool.QueueUserWorkItem calls</item>
/// </list>
/// During starvation, all of these compete for limited threads. By using a dedicated
/// thread, this probe can reliably measure latency without being affected by the
/// starvation it's trying to detect.
/// </para>
/// <para>
/// The probe measures end-to-end request latency, which includes:
/// <list type="bullet">
/// <item>Time waiting in the thread pool queue (the starvation indicator)</item>
/// <item>Actual request processing time</item>
/// <item>Network overhead (minimal for localhost)</item>
/// </list>
/// </para>
/// </remarks>
public class LatencyProbeService : IHostedService, IDisposable
{
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LatencyProbeService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServer _server;
    private readonly ISimulationTracker _simulationTracker;

    private Thread? _probeThread;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private string? _baseUrl;

    /// <summary>
    /// Probe interval in milliseconds. 100ms provides good granularity for
    /// observing latency changes during starvation ramp-up.
    /// </summary>
    private const int ProbeIntervalMs = 100;

    /// <summary>
    /// Request timeout in milliseconds. If the probe takes longer than this,
    /// it's recorded as a timeout with this value as the latency.
    /// Set to 30s to match the UI threshold for timeout detection.
    /// </summary>
    private const int RequestTimeoutMs = 30000;

    /// <summary>
    /// Initializes a new instance of the <see cref="LatencyProbeService"/> class.
    /// </summary>
    public LatencyProbeService(
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        IHttpClientFactory httpClientFactory,
        ILogger<LatencyProbeService> logger,
        IConfiguration configuration,
        IServer server,
        ISimulationTracker simulationTracker)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();

        // Get the server's actual listening address
        _baseUrl = GetProbeBaseUrl();

        // Create a dedicated thread (not from thread pool) for reliable probing
        _probeThread = new Thread(ProbeLoop)
        {
            Name = "LatencyProbeThread",
            IsBackground = true
        };
        _probeThread.Start(_cts.Token);

        _logger.LogInformation(
            "Latency probe service started. Interval: {Interval}ms, Timeout: {Timeout}ms, Target: {BaseUrl}",
            ProbeIntervalMs,
            RequestTimeoutMs,
            _baseUrl);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        // Give the thread a moment to exit gracefully
        _probeThread?.Join(TimeSpan.FromSeconds(2));

        _logger.LogInformation("Latency probe service stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Main probe loop running on a dedicated thread.
    /// </summary>
    private void ProbeLoop(object? state)
    {
        var cancellationToken = (CancellationToken)state!;

        // Wait for the server to fully start and accept connections
        Thread.Sleep(5000);

        // Get the base URL (may need to refresh after server starts)
        var baseUrl = _baseUrl ?? GetProbeBaseUrl();
        var isAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME"));
        _logger.LogInformation("Latency probe targeting: {BaseUrl}/api/health/probe (Azure: {IsAzure})", baseUrl, isAzure);

        // Create a handler configured for the environment
        var handler = new SocketsHttpHandler
        {
            // For Azure, allow connection pooling for better performance
            // For local, disable pooling to avoid socket reuse issues
            PooledConnectionLifetime = isAzure ? TimeSpan.FromMinutes(2) : TimeSpan.Zero,
            PooledConnectionIdleTimeout = isAzure ? TimeSpan.FromMinutes(1) : TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // Enable automatic decompression
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs)
        };

        // Add a user agent for Azure (some proxies require it)
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LatencyProbe/1.0");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check if Slow Request simulation is running
                bool isSlowRequestActive = _simulationTracker.GetActiveCountByType(Models.SimulationType.SlowRequest) > 0;
                
                // If Slow Request simulation is running, we slow down the probe frequency DRAMATICALLY
                // but we DO NOT stop it completely.
                //
                // Why?
                // 1. If we stop completely, the CLR Profiler often fails to record the "Request Finished" event
                //    for the slow request because the server becomes too quiet/idle, and ETW buffers don't flush.
                //    This results in "No events emitted" warnings and "Status: 0, Duration: [Infinite]" in traces.
                // 2. By keeping a "heartbeat" (e.g., every 5 seconds), we generate just enough activity
                //    to keep the trace pipeline alive without creating significant noise or contention.
                
                if (isSlowRequestActive)
                {
                    // Run sparsely: Wait 5 seconds between probes instead of 100ms
                    Thread.Sleep(5000);
                }

                var result = MeasureLatency(httpClient, cancellationToken);

                // Broadcast to all connected clients
                // Note: The UI filters out / hides the probe graph during slow request mode,
                // so these rare updates won't distract the user visually.
                BroadcastLatency(result);

                // Normal interval wait
                if (!isSlowRequestActive)
                {
                    Thread.Sleep(ProbeIntervalMs);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in latency probe loop");
                // Continue probing even after errors
                Thread.Sleep(ProbeIntervalMs);
            }
        }
    }
    /// <summary>
    /// Measures latency to the probe endpoint.
    /// </summary>
    private LatencyMeasurement MeasureLatency(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var timestamp = DateTimeOffset.UtcNow;
        bool isTimeout = false;
        bool isError = false;
        string? errorMessage = null;

        try
        {
            // Use synchronous HTTP call since we're on a dedicated thread
            // This is intentional - we don't want to use thread pool threads
            using var response = httpClient.GetAsync("/api/health/probe", cancellationToken)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                isError = true;
                errorMessage = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Check if it's a timeout (TaskCanceledException or OperationCanceledException when token not cancelled)
            if (ex is TaskCanceledException || 
               (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                isTimeout = true;
                _logger.LogWarning("Probe request timed out/cancelled");
            }
            else
            {
                isError = true;
                errorMessage = ex.Message;
                _logger.LogWarning(ex, "Probe request failed");
            }
        }

        // Always report the actual elapsed time, even on error/timeout
        // This ensures the chart shows the full impact of queuing (Total Time)
        return new LatencyMeasurement
        {
            Timestamp = timestamp,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            IsTimeout = isTimeout,
            IsError = isError,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// Broadcasts latency measurement to all connected SignalR clients.
    /// </summary>
    private void BroadcastLatency(LatencyMeasurement measurement)
    {
        try
        {
            // We're on a dedicated thread, so we can block waiting for the broadcast
            _hubContext.Clients.All.ReceiveLatency(measurement)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting latency measurement");
        }
    }

    /// <summary>
    /// Gets the base URL for the probe endpoint.
    /// </summary>
    private string GetProbeBaseUrl()
    {
        // Check if running in Azure App Service
        var websiteHostname = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        if (!string.IsNullOrEmpty(websiteHostname))
        {
            // Running in Azure App Service - use the public hostname
            // Azure provides HTTPS by default
            _logger.LogInformation("Detected Azure App Service environment: {Hostname}", websiteHostname);
            return $"https://{websiteHostname}";
        }

        // Check if running in a container with a custom hostname
        var containerHostname = Environment.GetEnvironmentVariable("CONTAINER_APP_HOSTNAME");
        if (!string.IsNullOrEmpty(containerHostname))
        {
            _logger.LogInformation("Detected Container Apps environment: {Hostname}", containerHostname);
            return $"https://{containerHostname}";
        }

        // Try to get the actual server addresses from IServer (works for local development)
        try
        {
            var addressFeature = _server.Features.Get<IServerAddressesFeature>();
            if (addressFeature?.Addresses.Count > 0)
            {
                // Prefer http over https for local probing (avoid SSL overhead)
                var httpAddress = addressFeature.Addresses.FirstOrDefault(a => a.StartsWith("http://"));
                if (!string.IsNullOrEmpty(httpAddress))
                {
                    // Replace wildcard with localhost
                    return httpAddress.Replace("*", "localhost").Replace("+", "localhost").Replace("[::]", "localhost");
                }
                
                var firstAddress = addressFeature.Addresses.First();
                return firstAddress.Replace("*", "localhost").Replace("+", "localhost").Replace("[::]", "localhost");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get server addresses from IServer feature");
        }

        // Try to get from configuration
        var urls = _configuration["Urls"];
        if (!string.IsNullOrEmpty(urls))
        {
            var firstUrl = urls.Split(';')[0].Trim();
            if (!string.IsNullOrEmpty(firstUrl))
            {
                return firstUrl.Replace("*", "localhost").Replace("+", "localhost");
            }
        }

        // Check ASPNETCORE_URLS environment variable
        var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrEmpty(envUrls))
        {
            var firstUrl = envUrls.Split(';')[0].Trim();
            if (!string.IsNullOrEmpty(firstUrl))
            {
                return firstUrl.Replace("*", "localhost").Replace("+", "localhost");
            }
        }

        // Check applicationUrl from launchSettings (common development scenario)
        var appUrl = _configuration["applicationUrl"];
        if (!string.IsNullOrEmpty(appUrl))
        {
            return appUrl.Split(';')[0].Trim();
        }

        // Default to localhost on the common development port
        return "http://localhost:5221";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a single latency measurement from the probe.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This data structure captures not just the latency
/// but also whether the request timed out or errored. During thread pool starvation:
/// </para>
/// <list type="bullet">
/// <item>Normal baseline latency: ~5-20ms</item>
/// <item>Mild starvation: 100-500ms</item>
/// <item>Severe starvation: 1,000-10,000ms</item>
/// <item>Critical starvation: Timeout (30,000ms)</item>
/// </list>
/// </remarks>
public class LatencyMeasurement
{
    /// <summary>
    /// When this measurement was taken.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Measured latency in milliseconds.
    /// If the request timed out, this equals the timeout value.
    /// </summary>
    public long LatencyMs { get; init; }

    /// <summary>
    /// Whether the request timed out.
    /// </summary>
    public bool IsTimeout { get; init; }

    /// <summary>
    /// Whether the request failed with an error.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// Error message if IsError is true.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
