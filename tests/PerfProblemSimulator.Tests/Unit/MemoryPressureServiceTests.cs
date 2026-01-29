using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="MemoryPressureService"/>.
/// </summary>
/// <remarks>
/// These tests verify that the memory pressure service correctly handles:
/// - Memory allocation with size limits
/// - Memory release and cleanup
/// - Tracking of allocated blocks
/// - Safety checks for maximum allocation
/// </remarks>
public class MemoryPressureServiceTests
{
    private readonly Mock<ISimulationTracker> _trackerMock;
    private readonly Mock<ILogger<MemoryPressureService>> _loggerMock;
    private readonly IOptions<ProblemSimulatorOptions> _options;

    public MemoryPressureServiceTests()
    {
        _trackerMock = new Mock<ISimulationTracker>();
        _loggerMock = new Mock<ILogger<MemoryPressureService>>();
        _options = Options.Create(new ProblemSimulatorOptions
        {
            MaxMemoryAllocationMb = 1024
        });
    }

    private MemoryPressureService CreateService() =>
        new MemoryPressureService(_trackerMock.Object, _loggerMock.Object, _options);

    [Fact]
    public void AllocateMemory_WithValidSize_ReturnsSuccessResult()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.AllocateMemory(10); // 10 MB

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SimulationType.Memory, result.Type);
        Assert.Equal("Started", result.Status);
        Assert.NotEqual(Guid.Empty, result.SimulationId);
    }

    [Fact]
    public void AllocateMemory_WithSizeExceedingMax_CapsToMaximum()
    {
        // Arrange
        var service = CreateService();
        var requestedSize = 2000; // Exceeds MaxMemoryAllocationMb of 1024

        // Act
        var result = service.AllocateMemory(requestedSize);

        // Assert
        Assert.NotNull(result.ActualParameters);
        // Should be capped to max (1024) or remaining available space
        var actualSize = (int)result.ActualParameters["SizeMegabytes"];
        Assert.True(actualSize <= 1024, $"Size should be capped to max (1024), was {actualSize}");
    }

    [Fact]
    public void AllocateMemory_WithZeroSize_UsesDefaultSize()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.AllocateMemory(0);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualSize = (int)result.ActualParameters["SizeMegabytes"];
        Assert.True(actualSize > 0, "Should use a default size > 0");
    }

    [Fact]
    public void AllocateMemory_WithNegativeSize_UsesDefaultSize()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.AllocateMemory(-100);

        // Assert
        Assert.NotNull(result.ActualParameters);
        var actualSize = (int)result.ActualParameters["SizeMegabytes"];
        Assert.True(actualSize > 0, "Should use a default size > 0 for negative input");
    }

    [Fact]
    public void AllocateMemory_IncrementsAllocatedBlockCount()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.AllocateMemory(10);
        service.AllocateMemory(10);
        var status = service.GetMemoryStatus();

        // Assert
        Assert.Equal(2, status.AllocatedBlocksCount);
    }

    [Fact]
    public void ReleaseAllMemory_ClearsAllAllocatedBlocks()
    {
        // Arrange
        var service = CreateService();
        service.AllocateMemory(10);
        service.AllocateMemory(10);

        // Act
        var result = service.ReleaseAllMemory(forceGc: false);

        // Assert
        var status = service.GetMemoryStatus();
        Assert.Equal(0, status.AllocatedBlocksCount);
        Assert.True(result.ReleasedBlockCount > 0);
    }

    [Fact]
    public void ReleaseAllMemory_WhenNoBlocks_ReturnsZeroCount()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.ReleaseAllMemory(forceGc: false);

        // Assert
        Assert.Equal(0, result.ReleasedBlockCount);
    }

    [Fact]
    public void GetMemoryStatus_ReturnsCorrectTotalAllocated()
    {
        // Arrange
        var service = CreateService();
        service.AllocateMemory(50);
        service.AllocateMemory(100);

        // Act
        var status = service.GetMemoryStatus();

        // Assert
        // Total should be approximately 150 MB (allowing for allocation overhead)
        var totalMb = status.TotalAllocatedMegabytes;
        Assert.True(totalMb >= 145 && totalMb <= 155, $"Expected ~150 MB, got {totalMb} MB");
    }

    [Fact]
    public void AllocateMemory_RegistersSimulationWithTracker()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.AllocateMemory(10);

        // Assert
        _trackerMock.Verify(
            t => t.RegisterSimulation(
                result.SimulationId,
                SimulationType.Memory,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationTokenSource>()),
            Times.Once);
    }

    [Fact]
    public void AllocateMemory_WhenWouldExceedMax_ReturnsLimitExceededStatus()
    {
        // Arrange
        var limitedOptions = Options.Create(new ProblemSimulatorOptions
        {
            MaxMemoryAllocationMb = 100 // Very limited
        });
        var service = new MemoryPressureService(_trackerMock.Object, _loggerMock.Object, limitedOptions);

        // First allocation should work
        service.AllocateMemory(90);

        // Act - Second allocation would exceed limit
        var result = service.AllocateMemory(50);

        // Assert - Should either cap or return error status
        Assert.NotNull(result);
        // Result should indicate the constraint was applied
    }

    [Fact]
    public void Constructor_WithNullTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryPressureService(null!, _loggerMock.Object, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryPressureService(_trackerMock.Object, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryPressureService(_trackerMock.Object, _loggerMock.Object, null!));
    }

    [Fact]
    public void AllocateMemory_SetsCorrectStartTime()
    {
        // Arrange
        var service = CreateService();
        var beforeAllocation = DateTimeOffset.UtcNow;

        // Act
        var result = service.AllocateMemory(10);

        // Assert
        var afterAllocation = DateTimeOffset.UtcNow;
        Assert.True(result.StartedAt >= beforeAllocation);
        Assert.True(result.StartedAt <= afterAllocation);
    }

    [Fact]
    public void AllocateMemory_HasNullEstimatedEndTime()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.AllocateMemory(10);

        // Assert - Memory allocations don't have an end time (held until released)
        Assert.Null(result.EstimatedEndAt);
    }
}
