using Microsoft.Extensions.Logging;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="ThreadBlockService"/>.
/// </summary>
/// <remarks>
/// These tests verify that the thread blocking service correctly handles:
/// - Parameter validation
/// - Proper simulation tracking
/// </remarks>
public class ThreadBlockServiceTests
{
    private readonly Mock<ISimulationTracker> _trackerMock;
    private readonly Mock<ILogger<ThreadBlockService>> _loggerMock;

    public ThreadBlockServiceTests()
    {
        _trackerMock = new Mock<ISimulationTracker>();
        _loggerMock = new Mock<ILogger<ThreadBlockService>>();
    }

    private ThreadBlockService CreateService() =>
        new ThreadBlockService(_trackerMock.Object, _loggerMock.Object);

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
    public async Task TriggerSyncOverAsyncAsync_WithLargeDelay_UsesRequestedDelay()
    {
        // Arrange
        var service = CreateService();
        var requestedDelay = 60000; // No limits now

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(requestedDelay, 1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDelay = (int)result.ActualParameters["DelayMilliseconds"];
        Assert.Equal(60000, actualDelay); // Uses requested value
    }

    [Fact]
    public async Task TriggerSyncOverAsyncAsync_WithLargeConcurrency_UsesRequestedConcurrency()
    {
        // Arrange
        var service = CreateService();
        var requestedConcurrency = 500; // No limits now

        // Act
        var result = await service.TriggerSyncOverAsyncAsync(100, requestedConcurrency, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualConcurrency = (int)result.ActualParameters["ConcurrentRequests"];
        Assert.Equal(500, actualConcurrency); // Uses requested value
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
            new ThreadBlockService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ThreadBlockService(_trackerMock.Object, null!));
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
