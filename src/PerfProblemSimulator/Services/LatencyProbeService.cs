using Microsoft.AspNetCore.SignalR;
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

    private Thread? _probeThread;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Probe interval in milliseconds. 100ms provides good granularity for
    /// observing latency changes during starvation ramp-up.
    /// </summary>
    private const int ProbeIntervalMs = 100;

    /// <summary>
    /// Request timeout in milliseconds. If the probe takes longer than this,
    /// it's recorded as a timeout with this value as the latency.
    /// </summary>
    private const int RequestTimeoutMs = 30000;

    /// <summary>
    /// Initializes a new instance of the <see cref="LatencyProbeService"/> class.
    /// </summary>
    public LatencyProbeService(
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        IHttpClientFactory httpClientFactory,
        ILogger<LatencyProbeService> logger,
        IConfiguration configuration)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();

        // Create a dedicated thread (not from thread pool) for reliable probing
        _probeThread = new Thread(ProbeLoop)
        {
            Name = "LatencyProbeThread",
            IsBackground = true
        };
        _probeThread.Start(_cts.Token);

        _logger.LogInformation(
            "Latency probe service started. Interval: {Interval}ms, Timeout: {Timeout}ms",
            ProbeIntervalMs,
            RequestTimeoutMs);

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

        // Get the base URL from configuration or use default
        var baseUrl = GetProbeBaseUrl();
        _logger.LogInformation("Latency probe targeting: {BaseUrl}/api/health/probe", baseUrl);

        // Create HttpClient with timeout
        using var httpClient = _httpClientFactory.CreateClient("LatencyProbe");
        httpClient.BaseAddress = new Uri(baseUrl);
        httpClient.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = MeasureLatency(httpClient, cancellationToken);

                // Broadcast to all connected clients
                BroadcastLatency(result);

                // Wait for next probe interval
                Thread.Sleep(ProbeIntervalMs);
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
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timed out (not service shutdown)
            stopwatch.Stop();
            isTimeout = true;
            _logger.LogWarning("Probe request timed out after {Timeout}ms", RequestTimeoutMs);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            isError = true;
            errorMessage = ex.Message;
            _logger.LogWarning(ex, "Probe request failed");
        }

        // If timeout, report the timeout value as the latency
        var latencyMs = isTimeout ? RequestTimeoutMs : stopwatch.ElapsedMilliseconds;

        return new LatencyMeasurement
        {
            Timestamp = timestamp,
            LatencyMs = latencyMs,
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
            // Fire and forget - we're on a dedicated thread, don't block
            _hubContext.Clients.All.ReceiveLatency(measurement);
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
        // Try to get from configuration, otherwise use sensible defaults
        var urls = _configuration["Urls"];
        if (!string.IsNullOrEmpty(urls))
        {
            // Take the first URL if multiple are configured
            var firstUrl = urls.Split(';')[0].Trim();
            if (!string.IsNullOrEmpty(firstUrl))
            {
                return firstUrl;
            }
        }

        // Check ASPNETCORE_URLS environment variable
        var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrEmpty(envUrls))
        {
            var firstUrl = envUrls.Split(';')[0].Trim();
            if (!string.IsNullOrEmpty(firstUrl))
            {
                return firstUrl;
            }
        }

        // Default to localhost on standard ports
        return "http://localhost:5000";
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
