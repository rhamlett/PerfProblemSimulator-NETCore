using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Controllers;

/// <summary>
/// Administrative endpoints for managing the simulator.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong>
/// </para>
/// <para>
/// This controller provides "escape hatch" functionality for resetting the
/// application state. In production applications, similar administrative
/// endpoints should be:
/// </para>
/// <list type="bullet">
/// <item>Protected by authentication/authorization</item>
/// <item>Rate-limited to prevent abuse</item>
/// <item>Audited/logged for compliance</item>
/// <item>Potentially disabled in production via feature flags</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[RequestTimeout("NoTimeout")] // Admin endpoints must always respond
public class AdminController : ControllerBase
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly TelemetryClient? _telemetryClient;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(
        ISimulationTracker simulationTracker,
        IMemoryPressureService memoryPressureService,
        ILogger<AdminController> logger,
        IServiceProvider serviceProvider)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _logger = logger;
        // Safely try to get TelemetryClient - may not be registered if App Insights not configured
        _telemetryClient = serviceProvider.GetService<TelemetryClient>();
    }

    /// <summary>
    /// Gets current simulation statistics.
    /// </summary>
    /// <returns>Statistics about active and historical simulations.</returns>
    /// <response code="200">Returns simulation statistics.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(SimulationStats), StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var activeSimulations = _simulationTracker.GetActiveSimulations();
        var memoryStatus = _memoryPressureService.GetMemoryStatus();

        ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

        return Ok(new SimulationStats
        {
            ActiveSimulationCount = activeSimulations.Count,
            SimulationsByType = activeSimulations
                .GroupBy(s => s.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            MemoryAllocated = new MemoryStats
            {
                BlockCount = memoryStatus.AllocatedBlocksCount,
                TotalBytes = memoryStatus.TotalAllocatedBytes,
                TotalMegabytes = memoryStatus.TotalAllocatedBytes / (1024.0 * 1024.0)
            },
            ThreadPool = new ThreadPoolStats
            {
                AvailableWorkerThreads = availableWorker,
                MaxWorkerThreads = maxWorker,
                UsedWorkerThreads = maxWorker - availableWorker,
                AvailableIoThreads = availableIo,
                MaxIoThreads = maxIo,
                PendingWorkItems = ThreadPool.PendingWorkItemCount
            },
            ProcessInfo = new ProcessStats
            {
                ProcessorCount = Environment.ProcessorCount,
                WorkingSetBytes = Environment.WorkingSet,
                ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                AzureSku = Environment.GetEnvironmentVariable("WEBSITE_SKU") ?? "Local",
                ComputeMode = Environment.GetEnvironmentVariable("WEBSITE_COMPUTE_MODE"),
                ComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME")
            }
        });
    }

    /// <summary>
    /// Tests Application Insights telemetry by sending a test event and trace.
    /// Use this endpoint to verify Application Insights is configured correctly.
    /// </summary>
    /// <returns>Diagnostic information about Application Insights configuration.</returns>
    /// <response code="200">Returns telemetry diagnostic information.</response>
    [HttpGet("test-appinsights")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TestAppInsights()
    {
        try
        {
            var testId = Guid.NewGuid();
            
            // Get connection string safely
            string? connectionString = null;
            try
            {
                var cs = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
                connectionString = cs != null && cs.Length > 20 ? cs[..20] + "..." : cs;
            }
            catch { /* ignore */ }

            var diagnostics = new AppInsightsDiagnostics
            {
                TestId = testId,
                TelemetryClientResolved = _telemetryClient != null,
                TelemetryClientEnabled = false,
                ConnectionString = connectionString
            };

            // Safely check IsEnabled
            try
            {
                diagnostics.TelemetryClientEnabled = _telemetryClient?.IsEnabled() ?? false;
            }
            catch (Exception ex)
            {
                diagnostics.Error = $"IsEnabled() threw: {ex.Message}";
            }

            // Log a test trace via ILogger
            _logger.LogWarning("🧪 TEST TRACE via ILogger - TestId: {TestId}", testId);
            
            // Send a test event via TelemetryClient
            if (_telemetryClient != null && diagnostics.TelemetryClientEnabled)
            {
                var errors = new List<string>();
                
                // Step 1: Simple TrackEvent (no properties)
                try
                {
                    _telemetryClient.TrackEvent("TestEvent_Simple");
                    diagnostics.TestEventSent = true;
                }
                catch (Exception ex)
                {
                    errors.Add($"TrackEvent(simple): {ex.GetType().Name}: {ex.Message}");
                }
                
                // Step 2: TrackEvent with properties
                try
                {
                    var props = new Dictionary<string, string>
                    {
                        ["TestId"] = testId.ToString(),
                        ["Source"] = "AdminController"
                    };
                    _telemetryClient.TrackEvent("TestEvent_WithProps", props);
                }
                catch (Exception ex)
                {
                    errors.Add($"TrackEvent(props): {ex.GetType().Name}: {ex.Message}");
                }
                
                // Step 3: TrackTrace
                try
                {
                    _telemetryClient.TrackTrace($"TestTrace - TestId: {testId}");
                }
                catch (Exception ex)
                {
                    errors.Add($"TrackTrace: {ex.GetType().Name}: {ex.Message}");
                }
                
                // Step 4: Flush
                try
                {
                    _telemetryClient.Flush();
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    errors.Add($"Flush: {ex.GetType().Name}: {ex.Message}");
                }
                
                if (errors.Count > 0)
                {
                    diagnostics.Error = string.Join(" | ", errors);
                }
                
                _logger.LogWarning("🧪 TEST complete - TestId: {TestId}, Errors: {ErrorCount}", testId, errors.Count);
            }
            else
            {
                diagnostics.TestEventSent = false;
            }

            diagnostics.KqlQueryForTrace = $"AppTraces | where TimeGenerated > ago(5m) | where Message contains \"{testId}\"";
            diagnostics.KqlQueryForEvent = $"AppEvents | where TimeGenerated > ago(5m) | where Name == \"TestEvent\"";

            return Ok(diagnostics);
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}

/// <summary>
/// Current simulation statistics.
/// </summary>
public class SimulationStats
{
    /// <summary>
    /// Total number of active simulations.
    /// </summary>
    public int ActiveSimulationCount { get; init; }

    /// <summary>
    /// Breakdown of simulations by type.
    /// </summary>
    public Dictionary<string, int> SimulationsByType { get; init; } = new();

    /// <summary>
    /// Memory allocation statistics.
    /// </summary>
    public required MemoryStats MemoryAllocated { get; init; }

    /// <summary>
    /// Thread pool statistics.
    /// </summary>
    public required ThreadPoolStats ThreadPool { get; init; }

    /// <summary>
    /// Process information.
    /// </summary>
    public required ProcessStats ProcessInfo { get; init; }
}

/// <summary>
/// Memory allocation statistics.
/// </summary>
public class MemoryStats
{
    /// <summary>
    /// Number of allocated memory blocks.
    /// </summary>
    public int BlockCount { get; init; }

    /// <summary>
    /// Total allocated bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Total allocated megabytes.
    /// </summary>
    public double TotalMegabytes { get; init; }
}

/// <summary>
/// Thread pool statistics.
/// </summary>
public class ThreadPoolStats
{
    /// <summary>
    /// Available worker threads.
    /// </summary>
    public int AvailableWorkerThreads { get; init; }

    /// <summary>
    /// Maximum worker threads.
    /// </summary>
    public int MaxWorkerThreads { get; init; }

    /// <summary>
    /// Currently used worker threads.
    /// </summary>
    public int UsedWorkerThreads { get; init; }

    /// <summary>
    /// Available I/O completion threads.
    /// </summary>
    public int AvailableIoThreads { get; init; }

    /// <summary>
    /// Maximum I/O completion threads.
    /// </summary>
    public int MaxIoThreads { get; init; }

    /// <summary>
    /// Number of pending work items in the queue.
    /// </summary>
    public long PendingWorkItems { get; init; }
}

    /// <summary>
    /// Process information statistics.
    /// </summary>
    public class ProcessStats
    {
        /// <summary>
        /// Number of processors available.
        /// </summary>
        public int ProcessorCount { get; init; }

        /// <summary>
        /// Process working set in bytes.
        /// </summary>
        public long WorkingSetBytes { get; init; }

        /// <summary>
        /// Managed heap size in bytes.
        /// </summary>
        public long ManagedHeapBytes { get; init; }

        /// <summary>
        /// The Azure SKU (Pricing Tier) if running in Azure App Service (e.g., P0V3, Standard, Basic).
        /// </summary>
        public string? AzureSku { get; init; }

        /// <summary>
        /// The compute mode (e.g., Dedicated, Shared).
        /// </summary>
        public string? ComputeMode { get; init; }

        /// <summary>
        /// The computer/worker name (from COMPUTERNAME environment variable).
        /// </summary>
        public string? ComputerName { get; init; }
    }

/// <summary>
/// Diagnostic information about Application Insights configuration.
/// </summary>
public class AppInsightsDiagnostics
{
    /// <summary>
    /// Unique identifier for this test run.
    /// </summary>
    public Guid TestId { get; set; }

    /// <summary>
    /// Whether TelemetryClient was resolved from DI.
    /// </summary>
    public bool TelemetryClientResolved { get; set; }

    /// <summary>
    /// Whether TelemetryClient.IsEnabled() returns true.
    /// </summary>
    public bool TelemetryClientEnabled { get; set; }

    /// <summary>
    /// Truncated connection string (for verification).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Whether a test event was sent.
    /// </summary>
    public bool TestEventSent { get; set; }

    /// <summary>
    /// Any error message encountered during the test.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// KQL query to find the test trace in AppTraces.
    /// </summary>
    public string? KqlQueryForTrace { get; set; }

    /// <summary>
    /// KQL query to find the test event in AppEvents.
    /// </summary>
    public string? KqlQueryForEvent { get; set; }
}
