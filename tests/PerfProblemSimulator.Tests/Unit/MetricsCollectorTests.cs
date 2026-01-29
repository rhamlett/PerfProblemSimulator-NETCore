using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the MetricsCollector service.
/// </summary>
public class MetricsCollectorTests : IDisposable
{
    private readonly Mock<ISimulationTracker> _mockTracker;
    private readonly Mock<IMemoryPressureService> _mockMemoryService;
    private readonly Mock<ILogger<MetricsCollector>> _mockLogger;
    private readonly IOptions<ProblemSimulatorOptions> _options;
    private MetricsCollector? _sut;

    public MetricsCollectorTests()
    {
        _mockTracker = new Mock<ISimulationTracker>();
        _mockMemoryService = new Mock<IMemoryPressureService>();
        _mockLogger = new Mock<ILogger<MetricsCollector>>();
        _options = Options.Create(new ProblemSimulatorOptions
        {
            MetricsCollectionIntervalMs = 100 // Fast collection for tests
        });

        // Setup default mock behaviors
        _mockTracker.Setup(t => t.ActiveCount).Returns(0);
        _mockTracker.Setup(t => t.GetActiveSimulations()).Returns(new List<ActiveSimulationInfo>());
        _mockMemoryService.Setup(m => m.GetMemoryStatus()).Returns(new MemoryStatus
        {
            AllocatedBlocksCount = 0,
            TotalAllocatedBytes = 0
        });
    }

    public void Dispose()
    {
        _sut?.Dispose();
    }

    [Fact]
    public void Constructor_WithNullSimulationTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MetricsCollector(
            null!,
            _mockMemoryService.Object,
            _mockLogger.Object,
            _options));
    }

    [Fact]
    public void Constructor_WithNullMemoryService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MetricsCollector(
            _mockTracker.Object,
            null!,
            _mockLogger.Object,
            _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MetricsCollector(
            _mockTracker.Object,
            _mockMemoryService.Object,
            null!,
            _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MetricsCollector(
            _mockTracker.Object,
            _mockMemoryService.Object,
            _mockLogger.Object,
            null!));
    }

    [Fact]
    public void Start_WhenNotRunning_StartsMetricsCollection()
    {
        // Arrange
        _sut = CreateCollector();
        var eventFired = false;
        _sut.MetricsCollected += (_, _) => eventFired = true;

        // Act
        _sut.Start();

        // Wait for at least one collection cycle
        Thread.Sleep(200);

        // Assert
        Assert.True(eventFired, "MetricsCollected event should have fired");
    }

    [Fact]
    public void Start_WhenAlreadyRunning_DoesNotStartAgain()
    {
        // Arrange
        _sut = CreateCollector();
        var eventCount = 0;
        _sut.MetricsCollected += (_, _) => Interlocked.Increment(ref eventCount);

        // Act
        _sut.Start();
        _sut.Start(); // Second start should be ignored

        // Wait for collection
        Thread.Sleep(250);

        // Assert - should have events from only one collection loop
        Assert.True(eventCount > 0 && eventCount <= 3, "Should only have 1-3 events");
    }

    [Fact]
    public void Stop_WhenRunning_StopsMetricsCollection()
    {
        // Arrange
        _sut = CreateCollector();
        _sut.Start();
        Thread.Sleep(150);

        // Act
        _sut.Stop();
        var countAfterStop = 0;
        _sut.MetricsCollected += (_, _) => Interlocked.Increment(ref countAfterStop);
        Thread.Sleep(200);

        // Assert
        Assert.Equal(0, countAfterStop);
    }

    [Fact]
    public void LatestSnapshot_AfterStart_ReturnsValidSnapshot()
    {
        // Arrange
        _sut = CreateCollector();

        // Act
        _sut.Start();
        Thread.Sleep(200);
        var snapshot = _sut.LatestSnapshot;
        _sut.Stop();

        // Assert
        Assert.True(snapshot.Timestamp > DateTimeOffset.MinValue, "Timestamp should be set");
        Assert.True(snapshot.CpuPercent >= 0 && snapshot.CpuPercent <= 100, "CPU should be 0-100");
        Assert.True(snapshot.WorkingSetMb > 0, "WorkingSet should be positive");
        Assert.True(snapshot.GcHeapMb >= 0, "GC heap should be non-negative");
    }

    [Fact]
    public void GetHealthStatus_WhenHealthy_ReturnsValidStatus()
    {
        // Arrange
        _sut = CreateCollector();
        _sut.Start();
        Thread.Sleep(200);

        // Act
        var status = _sut.GetHealthStatus();
        _sut.Stop();

        // Assert - verify structure is correctly populated
        // Note: IsHealthy may be false if the machine is under load
        Assert.NotNull(status.Cpu);
        Assert.NotNull(status.Memory);
        Assert.NotNull(status.ThreadPool);
        Assert.NotNull(status.Warnings);
        Assert.NotNull(status.ActiveSimulations);
        Assert.True(status.Timestamp > DateTimeOffset.MinValue, "Timestamp should be set");
    }

    [Fact]
    public void GetHealthStatus_WithActiveSimulations_IncludesSimulationsInStatus()
    {
        // Arrange
        var activeSimulations = new List<ActiveSimulationInfo>
        {
            new() 
            { 
                Id = Guid.NewGuid(), 
                Type = SimulationType.Cpu, 
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Parameters = new Dictionary<string, object>()
            }
        };
        _mockTracker.Setup(t => t.GetActiveSimulations()).Returns(activeSimulations);

        _sut = CreateCollector();
        _sut.Start();
        Thread.Sleep(200);

        // Act
        var status = _sut.GetHealthStatus();
        _sut.Stop();

        // Assert
        Assert.Single(status.ActiveSimulations);
        Assert.Equal(SimulationType.Cpu, status.ActiveSimulations[0].Type);
    }

    [Fact]
    public void MetricsCollected_Event_FiresWithSnapshot()
    {
        // Arrange
        _sut = CreateCollector();
        MetricsSnapshot? receivedSnapshot = null;
        _sut.MetricsCollected += (_, snapshot) => receivedSnapshot = snapshot;

        // Act
        _sut.Start();
        Thread.Sleep(200);
        _sut.Stop();

        // Assert
        Assert.NotNull(receivedSnapshot);
        Assert.True(receivedSnapshot.Value.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public void GetHealthStatus_ThreadPoolMetrics_ArePopulated()
    {
        // Arrange
        _sut = CreateCollector();
        _sut.Start();
        Thread.Sleep(200);

        // Act
        var status = _sut.GetHealthStatus();
        _sut.Stop();

        // Assert
        Assert.True(status.ThreadPool.MaxWorkerThreads > 0, "MaxWorkerThreads should be positive");
        Assert.True(status.ThreadPool.AvailableWorkerThreads > 0, "AvailableWorkerThreads should be positive");
        Assert.True(status.ThreadPool.MaxIoThreads > 0, "MaxIoThreads should be positive");
        Assert.True(status.ThreadPool.AvailableIoThreads > 0, "AvailableIoThreads should be positive");
    }

    private MetricsCollector CreateCollector()
    {
        return new MetricsCollector(
            _mockTracker.Object,
            _mockMemoryService.Object,
            _mockLogger.Object,
            _options);
    }
}
