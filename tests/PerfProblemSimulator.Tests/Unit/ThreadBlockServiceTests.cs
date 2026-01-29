using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="ThreadBlockService"/>.
/// </summary>
/// <remarks>
/// These tests verify that the thread blocking service correctly handles:
/// - Delay and concurrency limits
/// - Parameter validation
/// - Proper simulation tracking
/// </remarks>
public class ThreadBlockServiceTests
{
    private readonly Mock<ISimulationTracker> _trackerMock;
    private readonly Mock<ILogger<ThreadBlockService>> _loggerMock;
    private readonly IOptions<ProblemSimulatorOptions> _options;

    public ThreadBlockServiceTests()
    {
        _trackerMock = new Mock<ISimulationTracker>();
        _loggerMock = new Mock<ILogger<ThreadBlockService>>();
        _options = Options.Create(new ProblemSimulatorOptions
        {
            MaxThreadBlockDelayMs = 30000,
            MaxConcurrentBlockingRequests = 200
        });
    }

    private ThreadBlockService CreateService() =>
        new ThreadBlockService(_trackerMock.Object, _loggerMock.Object, _options);

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithValidParameters_ReturnsStartedResult()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SimulationType.ThreadBlock, result.Type);
        Assert.Equal("Started", result.Status);
        Assert.NotEqual(Guid.Empty, result.SimulationId);
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithDelayExceedingMax_CapsToMaximum()
    {
        // Arrange
        var service = CreateService();
        var requestedDelay = 60000; // Exceeds MaxThreadBlockDelayMs of 30000

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(requestedDelay, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDelay = (int)result.ActualParameters["DelayMilliseconds"];
        Assert.True(actualDelay <= 30000, $"Delay should be capped to max (30000), was {actualDelay}");
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithConcurrencyExceedingMax_CapsToMaximum()
    {
        // Arrange
        var service = CreateService();
        var requestedConcurrency = 500; // Exceeds MaxConcurrentBlockingRequests of 200

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, requestedConcurrency, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualConcurrency = (int)result.ActualParameters["ConcurrentRequests"];
        Assert.True(actualConcurrency <= 200, $"Concurrency should be capped to max (200), was {actualConcurrency}");
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithZeroDelay_UsesDefaultDelay()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(0, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDelay = (int)result.ActualParameters["DelayMilliseconds"];
        Assert.True(actualDelay > 0, "Should use a default delay > 0");
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithZeroConcurrency_UsesDefaultConcurrency()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, 0, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualConcurrency = (int)result.ActualParameters["ConcurrentRequests"];
        Assert.True(actualConcurrency > 0, "Should use a default concurrency > 0");
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_RegistersSimulationWithTracker()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, 1, CancellationToken.None);

        // Assert
        _trackerMock.Verify(
            t => t.RegisterSimulation(
                result.SimulationId,
                SimulationType.ThreadBlock,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationTokenSource>()),
            Times.Once);
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_IncludesThreadPoolInfoInParameters()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        Assert.True(result.ActualParameters.ContainsKey("DelayMilliseconds"));
        Assert.True(result.ActualParameters.ContainsKey("ConcurrentRequests"));
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_SetsCorrectStartAndEndTimes()
    {
        // Arrange
        var service = CreateService();
        var delayMs = 500;
        var concurrentRequests = 2;
        var beforeStart = DateTimeOffset.UtcNow;

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(delayMs, concurrentRequests, CancellationToken.None);

        // Assert
        var afterStart = DateTimeOffset.UtcNow;
        Assert.True(result.StartedAt >= beforeStart);
        Assert.True(result.StartedAt <= afterStart);

        Assert.NotNull(result.EstimatedEndAt);
    }

    [Fact]
    public void Constructor_WithNullTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ThreadBlockService(null!, _loggerMock.Object, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ThreadBlockService(_trackerMock.Object, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ThreadBlockService(_trackerMock.Object, _loggerMock.Object, null!));
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithNegativeDelay_UsesDefaultDelay()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(-1000, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDelay = (int)result.ActualParameters["DelayMilliseconds"];
        Assert.True(actualDelay > 0, "Should use a default delay > 0 for negative input");
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithNegativeConcurrency_UsesDefaultConcurrency()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, -5, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualConcurrency = (int)result.ActualParameters["ConcurrentRequests"];
        Assert.True(actualConcurrency > 0, "Should use a default concurrency > 0 for negative input");
    }
}
