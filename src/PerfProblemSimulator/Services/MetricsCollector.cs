using Microsoft.Extensions.Options;
using PerfProblemSimulator.Models;
using System.Diagnostics;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Collects system metrics on a dedicated thread for dashboard updates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note (FR-013 Implementation):</strong>
/// </para>
/// <para>
/// This service uses a dedicated <see cref="Thread"/> instead of <see cref="Task.Run"/>
/// for metrics collection. This is critical because:
/// </para>
/// <list type="number">
/// <item>
/// <term>Thread Pool Independence</term>
/// <description>
/// When we simulate thread pool starvation, all thread pool threads are blocked.
/// If metrics collection used the thread pool, the dashboard would freeze exactly
/// when you need it most - during thread starvation!
/// </description>
/// </item>
/// <item>
/// <term>Health Endpoint Responsiveness</term>
/// <description>
/// The /api/health endpoint relies on cached metrics from this collector.
/// Load balancers and monitoring systems need health checks to work even
/// when the application is under stress.
/// </description>
/// </item>
/// <item>
/// <term>Real-Time Dashboard</term>
/// <description>
/// SignalR broadcasts use the metrics from this collector. Users need to
/// see metrics updating to understand what's happening during simulations.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>Trade-off:</strong> Using a dedicated thread consumes one OS thread
/// permanently. This is acceptable for this educational tool but would need
/// consideration in production systems with many instances.
/// </para>
/// </remarks>
public class MetricsCollector : IMetricsCollector
{
    private readonly ISimulationTracker _simulationTracker;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly ILogger<MetricsCollector> _logger;
    private readonly ProblemSimulatorOptions _options;

    private Thread? _collectionThread;
    private volatile bool _running;
    private MetricsSnapshot _latestSnapshot;
    private readonly object _snapshotLock = new();

    // CPU measurement
    private readonly Process _currentProcess;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuMeasurement;

    /// <inheritdoc />
    public MetricsSnapshot LatestSnapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _latestSnapshot;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<MetricsSnapshot>? MetricsCollected;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsCollector"/> class.
    /// </summary>
    public MetricsCollector(
        ISimulationTracker simulationTracker,
        IMemoryPressureService memoryPressureService,
        ILogger<MetricsCollector> logger,
        IOptions<ProblemSimulatorOptions> options)
    {
        _simulationTracker = simulationTracker ?? throw new ArgumentNullException(nameof(simulationTracker));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;
        _lastCpuMeasurement = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_running) return;

        _running = true;

        // ==========================================================================
        // Create a DEDICATED thread (not thread pool!)
        // ==========================================================================
        // This is intentional - we need metrics even when the thread pool is starved.
        // Using Task.Run would put the work on the thread pool, which defeats the purpose.
        _collectionThread = new Thread(CollectionLoop)
        {
            Name = "MetricsCollector",
            IsBackground = true, // Won't prevent app shutdown
            Priority = ThreadPriority.BelowNormal // Don't compete with actual work
        };

        _collectionThread.Start();
        _logger.LogInformation("Metrics collector started on dedicated thread");
    }

    /// <inheritdoc />
    public void Stop()
    {
        _running = false;
        _collectionThread?.Join(TimeSpan.FromSeconds(2));
        _logger.LogInformation("Metrics collector stopped");
    }

    /// <inheritdoc />
    public ApplicationHealthStatus GetHealthStatus()
    {
        var snapshot = LatestSnapshot;
        var memoryStatus = _memoryPressureService.GetMemoryStatus();
        var activeSimulations = _simulationTracker.GetActiveSimulations();

        ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

        var warnings = new List<string>();

        // Check for concerning conditions
        if (snapshot.CpuPercent > 80)
            warnings.Add($"High CPU usage: {snapshot.CpuPercent:F1}%");

        if (snapshot.WorkingSetMb > 500)
            warnings.Add($"High memory usage: {snapshot.WorkingSetMb:F0} MB");

        if (availableWorker < maxWorker * 0.2)
            warnings.Add($"Low available worker threads: {availableWorker}/{maxWorker}");

        if (snapshot.ThreadPoolQueueLength > 10)
            warnings.Add($"Thread pool queue backing up: {snapshot.ThreadPoolQueueLength} pending items");

        return new ApplicationHealthStatus
        {
            Timestamp = snapshot.Timestamp,
            IsHealthy = warnings.Count == 0,
            Warnings = warnings,
            Cpu = new CpuMetrics
            {
                UsagePercent = snapshot.CpuPercent,
                ProcessorCount = Environment.ProcessorCount
            },
            Memory = new MemoryMetrics
            {
                WorkingSetBytes = (long)(snapshot.WorkingSetMb * 1024 * 1024),
                GcHeapBytes = (long)(snapshot.GcHeapMb * 1024 * 1024),
                AllocatedBlocksCount = memoryStatus.AllocatedBlocksCount,
                AllocatedBlocksTotalBytes = memoryStatus.TotalAllocatedBytes
            },
            ThreadPool = new ThreadPoolMetrics
            {
                ThreadCount = snapshot.ThreadPoolThreads,
                PendingWorkItems = snapshot.ThreadPoolQueueLength,
                CompletedWorkItems = ThreadPool.CompletedWorkItemCount,
                AvailableWorkerThreads = availableWorker,
                MaxWorkerThreads = maxWorker,
                AvailableIoThreads = availableIo,
                MaxIoThreads = maxIo
            },
            ActiveSimulations = activeSimulations.Select(s => new SimulationSummary
            {
                Id = s.Id,
                Type = s.Type,
                StartedAt = s.StartedAt,
                RunningDuration = DateTimeOffset.UtcNow - s.StartedAt
            }).ToList()
        };
    }

    /// <summary>
    /// Main collection loop running on dedicated thread.
    /// </summary>
    private void CollectionLoop()
    {
        _logger.LogDebug("Metrics collection loop started");

        while (_running)
        {
            try
            {
                var snapshot = CollectMetrics();

                lock (_snapshotLock)
                {
                    _latestSnapshot = snapshot;
                }

                // Raise event for SignalR broadcast
                MetricsCollected?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting metrics");
            }

            // Sleep on this thread (not using Task.Delay which uses thread pool!)
            Thread.Sleep(_options.MetricsCollectionIntervalMs);
        }

        _logger.LogDebug("Metrics collection loop ended");
    }

    /// <summary>
    /// Collects all metrics for a snapshot.
    /// </summary>
    private MetricsSnapshot CollectMetrics()
    {
        // Refresh process info
        _currentProcess.Refresh();

        // Calculate CPU usage
        var currentCpuTime = _currentProcess.TotalProcessorTime;
        var currentMeasurement = DateTime.UtcNow;
        var cpuUsed = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
        var elapsed = (currentMeasurement - _lastCpuMeasurement).TotalMilliseconds;
        var cpuPercent = elapsed > 0 ? (cpuUsed / elapsed / Environment.ProcessorCount) * 100 : 0;

        _lastCpuTime = currentCpuTime;
        _lastCpuMeasurement = currentMeasurement;

        // Get memory info
        var workingSetMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
        var gcHeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        // Get thread pool info
        ThreadPool.GetAvailableThreads(out var availableWorker, out _);
        ThreadPool.GetMaxThreads(out var maxWorker, out _);
        var threadPoolThreads = maxWorker - availableWorker;
        var queueLength = ThreadPool.PendingWorkItemCount;

        // Get active simulation count
        var activeCount = _simulationTracker.ActiveCount;

        return new MetricsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            CpuPercent = Math.Max(0, Math.Min(100, cpuPercent)),
            WorkingSetMb = workingSetMb,
            GcHeapMb = gcHeapMb,
            ThreadPoolThreads = threadPoolThreads,
            ThreadPoolQueueLength = queueLength,
            ActiveSimulationCount = activeCount
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _currentProcess.Dispose();
        GC.SuppressFinalize(this);
    }
}
