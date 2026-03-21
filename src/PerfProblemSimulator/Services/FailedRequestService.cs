using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using PerfProblemSimulator.Hubs;
using PerfProblemSimulator.Models;
using System.Diagnostics;
using System.Text.Json;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that generates failed HTTP requests (5xx responses) for AppLens testing.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// Generates HTTP 500 errors that appear in Azure AppLens and Application Insights.
/// This is useful for testing error detection, alerting, and diagnosis workflows.
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>Start() creates a dedicated spawner thread (not thread pool)</item>
/// <item>For each requested failure, make an HTTP POST to /api/loadtest</item>
/// <item>Load test parameters are configured for guaranteed failure:
///   <list type="bullet">
///     <item>baselineDelayMs: 1500 (ensures request takes >1s)</item>
///     <item>errorAfterSeconds: 1 (start error checks after 1 second)</item>
///     <item>errorPercent: 100 (100% probability = guaranteed failure)</item>
///   </list>
/// </item>
/// <item>The load test endpoint throws a random exception → HTTP 500</item>
/// <item>Each failure is logged and tracked for status reporting</item>
/// </list>
/// </para>
/// <para>
/// <strong>WHY USE LOAD TEST ENDPOINT:</strong>
/// The load test endpoint already has error injection logic. By setting errorPercent=100,
/// we guarantee that every request will fail with an exception. This produces authentic
/// HTTP 500 responses that pass through the full ASP.NET pipeline and appear correctly
/// in all Azure monitoring tools.
/// </para>
/// </remarks>
public class FailedRequestService : IFailedRequestService, IDisposable
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly ISimulationContext _simulationContext;
    private readonly ILogger<FailedRequestService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<MetricsHub, IMetricsClient> _hubContext;
    private readonly IServer _server;

    private CancellationTokenSource? _cts;
    private Thread? _requestSpawnerThread;
    private volatile bool _isRunning;
    private int _requestsSent;
    private int _requestsCompleted;
    private int _requestsInProgress;
    private int _targetCount;
    private DateTimeOffset? _startedAt;
    private Guid _simulationId;
    private string? _baseUrl;

    public bool IsRunning => _isRunning;

    public FailedRequestService(
        ISimulationTracker simulationTracker,
        ISimulationContext simulationContext,
        ILogger<FailedRequestService> logger,
        IHttpClientFactory httpClientFactory,
        IHubContext<MetricsHub, IMetricsClient> hubContext,
        IServer server)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _simulationContext = simulationContext ?? throw new ArgumentNullException(nameof(simulationContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public SimulationResult Start(int requestCount)
    {
        if (_isRunning)
        {
            return new SimulationResult
            {
                SimulationId = _simulationId,
                Type = SimulationType.FailedRequest,
                Status = "AlreadyRunning",
                Message = "Failed request simulation is already running. Stop it first or wait for completion."
            };
        }

        _simulationId = Guid.NewGuid();
        _cts = new CancellationTokenSource();
        _requestsSent = 0;
        _requestsCompleted = 0;
        _requestsInProgress = 0;
        _targetCount = Math.Max(1, requestCount);
        _startedAt = DateTimeOffset.UtcNow;
        _isRunning = true;

        // Get the base URL for HTTP calls
        _baseUrl = GetBaseUrl();

        var parameters = new Dictionary<string, object>
        {
            ["TargetCount"] = _targetCount
        };

        _simulationTracker.RegisterSimulation(_simulationId, SimulationType.FailedRequest, parameters, _cts);

        // Use dedicated thread (not thread pool) to spawn requests
        _requestSpawnerThread = new Thread(() => SpawnFailedRequestsLoop(_cts.Token))
        {
            Name = $"FailedRequestSpawner-{_simulationId:N}",
            IsBackground = true
        };
        _requestSpawnerThread.Start();

        _logger.LogWarning(
            "❌ Failed request simulation started: {SimulationId}. " +
            "Generating {Count} HTTP 500 errors at {BaseUrl}/api/loadtest",
            _simulationId, _targetCount, _baseUrl);

        return new SimulationResult
        {
            SimulationId = _simulationId,
            Type = SimulationType.FailedRequest,
            Status = "Started",
            Message = $"Generating {_targetCount} failed requests (HTTP 500 errors). " +
                      "These will appear in AppLens and Application Insights failure metrics. " +
                      "Each request takes ~1.5 seconds before failing.",
            ActualParameters = parameters,
            StartedAt = _startedAt.Value
        };
    }

    public SimulationResult Stop()
    {
        if (!_isRunning)
        {
            return new SimulationResult
            {
                SimulationId = Guid.Empty,
                Type = SimulationType.FailedRequest,
                Status = "NotRunning",
                Message = "No failed request simulation is running."
            };
        }

        _cts?.Cancel();
        _isRunning = false;
        _simulationTracker.UnregisterSimulation(_simulationId);

        _logger.LogInformation(
            "🛑 Failed request simulation stopped: {SimulationId}. " +
            "Sent={Sent}, Completed={Completed}, InProgress={InProgress}",
            _simulationId, _requestsSent, _requestsCompleted, _requestsInProgress);

        return new SimulationResult
        {
            SimulationId = _simulationId,
            Type = SimulationType.FailedRequest,
            Status = "Stopped",
            Message = $"Failed request simulation stopped. Sent: {_requestsSent}, " +
                      $"Completed: {_requestsCompleted}, Still in progress: {_requestsInProgress}",
            ActualParameters = new Dictionary<string, object>
            {
                ["RequestsSent"] = _requestsSent,
                ["RequestsCompleted"] = _requestsCompleted,
                ["TargetCount"] = _targetCount
            }
        };
    }

    public FailedRequestStatus GetStatus()
    {
        return new FailedRequestStatus
        {
            IsRunning = _isRunning,
            RequestsSent = _requestsSent,
            RequestsCompleted = _requestsCompleted,
            RequestsInProgress = _requestsInProgress,
            TargetCount = _targetCount,
            StartedAt = _startedAt
        };
    }

    /// <summary>
    /// Gets the base URL of the current application for self-calls.
    /// </summary>
    private string GetBaseUrl()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.FirstOrDefault();
        
        if (string.IsNullOrEmpty(address))
        {
            // Fallback to localhost
            return "http://localhost:5000";
        }

        // Replace wildcard with localhost
        return address.Replace("*", "localhost").Replace("+", "localhost").Replace("[::]", "localhost");
    }

    /// <summary>
    /// Main loop that spawns failed requests.
    /// </summary>
    private void SpawnFailedRequestsLoop(CancellationToken cancellationToken)
    {
        // Set simulation context for Application Insights telemetry correlation
        using var simulationScope = _simulationContext.SetContext(_simulationId, SimulationType.FailedRequest.ToString());
        
        try
        {
            _logger.LogInformation(
                "Failed request spawner started for simulation {SimulationId}",
                _simulationId);

            // Short initial delay to let any startup settle
            Thread.Sleep(500);

            while (!cancellationToken.IsCancellationRequested && _requestsSent < _targetCount)
            {
                // Fire the request asynchronously and continue to next
                _ = Task.Run(() => SendFailedRequest(cancellationToken), cancellationToken);
                
                Interlocked.Increment(ref _requestsSent);
                Interlocked.Increment(ref _requestsInProgress);

                // Small delay between requests to spread them out
                // This makes them more visible as individual data points
                Thread.Sleep(200);
            }

            // Wait for in-progress requests to complete (with timeout)
            var waitStart = DateTime.UtcNow;
            while (_requestsInProgress > 0 && (DateTime.UtcNow - waitStart).TotalSeconds < 30)
            {
                Thread.Sleep(500);
            }

            _logger.LogInformation(
                "Failed request simulation completed: {SimulationId}. " +
                "Sent={Sent}, Completed={Completed}",
                _simulationId, _requestsSent, _requestsCompleted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Failed request spawner cancelled for {SimulationId}", _simulationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in failed request spawner for {SimulationId}", _simulationId);
        }
        finally
        {
            _isRunning = false;
            _simulationTracker.UnregisterSimulation(_simulationId);

            _logger.LogInformation(
                "Failed request simulation {SimulationId} completed. Generated {Count} HTTP 500 errors.",
                _simulationId, _requestsCompleted);
        }
    }

    /// <summary>
    /// Sends a single request to the load test endpoint with error-guaranteed parameters.
    /// </summary>
    private async Task SendFailedRequest(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var client = _httpClientFactory.CreateClient("FailedRequest");
            client.BaseAddress = new Uri(_baseUrl!);
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string for GET request (LoadTestController uses [HttpGet] with [FromQuery] params)
            // Parameters tuned for guaranteed failure: 100% error rate after 1 second delay
            var queryParams = "?baselineDelayMs=1500" +
                             "&workIterations=500" +
                             "&bufferSizeKb=100" +
                             "&softLimit=10000" +
                             "&degradationFactor=0" +
                             "&errorAfter=1" +
                             "&errorPercent=100";

            _logger.LogDebug(
                "Sending failed request {RequestId} to {Url}",
                requestId, $"{_baseUrl}/api/loadtest{queryParams}");

            var response = await client.GetAsync($"/api/loadtest{queryParams}", cancellationToken);

            stopwatch.Stop();

            // Read the response body to extract error details
            string? errorType = null;
            if ((int)response.StatusCode >= 500)
            {
                try
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    errorType = ExtractExceptionType(responseBody);
                }
                catch
                {
                    // Ignore parsing errors
                }

                _logger.LogInformation(
                    "✓ Failed request {RequestId} completed with expected HTTP {Status} ({ErrorType}) in {Elapsed}ms",
                    requestId, (int)response.StatusCode, errorType ?? "Unknown", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                // If we get a non-5xx response, log it as unexpected
                _logger.LogWarning(
                    "⚠ Failed request {RequestId} got unexpected HTTP {Status} (expected 5xx) in {Elapsed}ms",
                    requestId, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
            }

            // Broadcast latency to dashboard for visibility
            await _hubContext.Clients.All.ReceiveLatency(new LatencyMeasurement
            {
                Timestamp = DateTimeOffset.UtcNow,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                IsTimeout = false,
                IsError = true,
                ErrorMessage = errorType ?? "Unknown Error",
                Source = "FailedRequest"
            });
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            // HTTP errors (like 500) that throw exceptions - this is expected!
            _logger.LogInformation(
                "✓ Failed request {RequestId} threw expected exception in {Elapsed}ms: {Message}",
                requestId, stopwatch.ElapsedMilliseconds, ex.Message);
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Failed request {RequestId} was cancelled", requestId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Failed request {RequestId} encountered unexpected error in {Elapsed}ms",
                requestId, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            Interlocked.Increment(ref _requestsCompleted);
            Interlocked.Decrement(ref _requestsInProgress);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    /// <summary>
    /// Extracts the exception type from the error response body.
    /// ASP.NET Core returns exception type in various formats depending on configuration.
    /// </summary>
    private static string? ExtractExceptionType(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        // Try to extract exception type from common patterns:
        // 1. "System.InvalidOperationException: ..." - plain text format
        // 2. {"type": "...", "title": "..."} - Problem Details JSON format
        // 3. {"error": "...", "message": "..."} - Custom ErrorResponse format

        // Check for plain text exception format (e.g., "System.InvalidOperationException: message")
        var colonIndex = responseBody.IndexOf(':');
        if (colonIndex > 0)
        {
            var potentialType = responseBody.Substring(0, colonIndex).Trim();
            // Check if it looks like an exception type (contains Exception or Error)
            if (potentialType.Contains("Exception") || potentialType.Contains("Error"))
            {
                // Extract just the class name (e.g., "InvalidOperationException" from "System.InvalidOperationException")
                var lastDot = potentialType.LastIndexOf('.');
                return lastDot >= 0 ? potentialType.Substring(lastDot + 1) : potentialType;
            }
        }

        // Try to parse as JSON and look for type or title field
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Problem Details format
            if (root.TryGetProperty("type", out var typeElement))
            {
                var typeStr = typeElement.GetString();
                if (!string.IsNullOrEmpty(typeStr) && typeStr.Contains("Exception"))
                {
                    var lastDot = typeStr.LastIndexOf('.');
                    return lastDot >= 0 ? typeStr.Substring(lastDot + 1) : typeStr;
                }
            }

            // Try title from Problem Details
            if (root.TryGetProperty("title", out var titleElement))
            {
                return titleElement.GetString();
            }

            // Custom ErrorResponse format
            if (root.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString();
            }
        }
        catch
        {
            // Not valid JSON, ignore
        }

        // Fallback: return first 50 chars of response as error type
        return responseBody.Length > 50 ? responseBody.Substring(0, 50) + "..." : responseBody;
    }
}
