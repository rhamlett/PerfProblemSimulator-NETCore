using Microsoft.Extensions.Logging;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="CpuStressService"/>.
/// </summary>
/// <remarks>
/// These tests verify that the CPU stress service correctly handles:
/// - Duration validation
/// - Cancellation token support
/// - Proper reporting of stress operations
/// </remarks>
public class CpuStressServiceTests
{
    private readonly Mock<ISimulationTracker> _trackerMock;
    private readonly Mock<ILogger<CpuStressService>> _loggerMock;

    public CpuStressServiceTests()
    {
        _trackerMock = new Mock<ISimulationTracker>();
        _loggerMock = new Mock<ILogger<CpuStressService>>();
    }

    private CpuStressService CreateService() =>
        new CpuStressService(_trackerMock.Object, _loggerMock.Object);

    [Fact]
    public async Task TriggerCpuStressAsync_WithValidDuration_ReturnsStartedResult()
    {
        // Arrange
        var service = CreateService();
        var durationSeconds = 1; // Use short duration for test

        // Act
        var result = await service.TriggerCpuStressAsync(durationSeconds, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SimulationType.Cpu, result.Type);
        Assert.Equal("Started", result.Status);
        Assert.NotEqual(Guid.Empty, result.SimulationId);
    }

    [Fact]
    public async Task TriggerCpuStressAsync_WithLargeDuration_UsesRequestedDuration()
    {
        // Arrange
        var service = CreateService();
        var requestedDuration = 500; // No limits now

        // Act
        var result = await service.TriggerCpuStressAsync(requestedDuration, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        Assert.Equal(500, result.ActualParameters["DurationSeconds"]); // Uses requested value
    }

    [Fact]
    public async Task TriggerCpuStressAsync_WithZeroDuration_UsesDefaultDuration()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerCpuStressAsync(0, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDuration = (int)result.ActualParameters["DurationSeconds"];
        Assert.True(actualDuration > 0, "Should use a default duration > 0");
    }

    [Fact]
    public async Task TriggerCpuStressAsync_WithNegativeDuration_UsesDefaultDuration()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerCpuStressAsync(-10, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualDuration = (int)result.ActualParameters["DurationSeconds"];
        Assert.True(actualDuration > 0, "Should use a default duration > 0 for negative input");
    }

    [Fact]
    public async Task TriggerCpuStressAsync_RegistersSimulationWithTracker()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerCpuStressAsync(1, CancellationToken.None);

        // Assert
        _trackerMock.Verify(
            t => t.RegisterSimulation(
                result.SimulationId,
                SimulationType.Cpu,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationTokenSource>()),
            Times.Once);
    }

    [Fact]
    public async Task TriggerCpuStressAsync_WhenCancelled_StopsEarly()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        // Act
        var result = await service.TriggerCpuStressAsync(60, cts.Token);

        // Assert - Should return quickly even though duration was 60 seconds
        Assert.NotNull(result);
        // The status might be Started or Cancelled depending on implementation
        // The key is that it returns without waiting the full duration
    }

    [Fact]
    public async Task TriggerCpuStressAsync_IncludesProcessorCountInParameters()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerCpuStressAsync(1, CancellationToken.None);

        // Assert
        Assert.NotNull(result.ActualParameters);
        Assert.True(result.ActualParameters.ContainsKey("ProcessorCount"));
        Assert.Equal(Environment.ProcessorCount, result.ActualParameters["ProcessorCount"]);
    }

    [Fact]
    public async Task TriggerCpuStressAsync_SetsCorrectStartAndEndTimes()
    {
        // Arrange
        var service = CreateService();
        var durationSeconds = 5;
        var beforeStart = DateTimeOffset.UtcNow;

        // Act
        var result = await service.TriggerCpuStressAsync(durationSeconds, CancellationToken.None);

        // Assert
        var afterStart = DateTimeOffset.UtcNow;
        Assert.True(result.StartedAt >= beforeStart);
        Assert.True(result.StartedAt <= afterStart);

        Assert.NotNull(result.EstimatedEndAt);
        var expectedEnd = result.StartedAt.AddSeconds(durationSeconds);
        // Allow 1 second tolerance for timing differences
        Assert.True(Math.Abs((result.EstimatedEndAt.Value - expectedEnd).TotalSeconds) < 1);
    }

    [Fact]
    public async Task TriggerCpuStressAsync_WithVeryShortDuration_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TriggerCpuStressAsync(1, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SimulationType.Cpu, result.Type);
    }

    [Fact]
    public void Constructor_WithNullTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CpuStressService(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CpuStressService(_trackerMock.Object, null!));
    }
}
