using Microsoft.Extensions.Logging;
using Moq;
using PerfProblemSimulator.Models;
using PerfProblemSimulator.Services;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="SimulationTracker"/> service.
/// </summary>
/// <remarks>
/// These tests verify the thread-safe simulation tracking functionality,
/// including registration, unregistration, querying, and cancellation.
/// </remarks>
public class SimulationTrackerTests
{
    private readonly Mock<ILogger<SimulationTracker>> _loggerMock;
    private readonly SimulationTracker _tracker;

    public SimulationTrackerTests()
    {
        _loggerMock = new Mock<ILogger<SimulationTracker>>();
        _tracker = new SimulationTracker(_loggerMock.Object);
    }

    [Fact]
    public void RegisterSimulation_WithValidParameters_AddsSimulation()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters = new Dictionary<string, object> { ["DurationSeconds"] = 30 };
        var cts = new CancellationTokenSource();

        // Act
        _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters, cts);

        // Assert
        Assert.Equal(1, _tracker.ActiveCount);
        Assert.True(_tracker.TryGetSimulation(simulationId, out var info));
        Assert.NotNull(info);
        Assert.Equal(simulationId, info.Id);
        Assert.Equal(SimulationType.Cpu, info.Type);
    }

    [Fact]
    public void RegisterSimulation_WithDuplicateId_DoesNotOverwrite()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters1 = new Dictionary<string, object> { ["DurationSeconds"] = 30 };
        var parameters2 = new Dictionary<string, object> { ["DurationSeconds"] = 60 };
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        // Act
        _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters1, cts1);
        _tracker.RegisterSimulation(simulationId, SimulationType.Memory, parameters2, cts2);

        // Assert
        Assert.Equal(1, _tracker.ActiveCount);
        Assert.True(_tracker.TryGetSimulation(simulationId, out var info));
        Assert.Equal(SimulationType.Cpu, info!.Type); // Original type should be preserved
    }

    [Fact]
    public void UnregisterSimulation_WhenExists_RemovesAndReturnsTrue()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters = new Dictionary<string, object> { ["Test"] = 1 };
        var cts = new CancellationTokenSource();
        _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters, cts);

        // Act
        var result = _tracker.UnregisterSimulation(simulationId);

        // Assert
        Assert.True(result);
        Assert.Equal(0, _tracker.ActiveCount);
        Assert.False(_tracker.TryGetSimulation(simulationId, out _));
    }

    [Fact]
    public void UnregisterSimulation_WhenDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var simulationId = Guid.NewGuid();

        // Act
        var result = _tracker.UnregisterSimulation(simulationId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetActiveSimulations_ReturnsAllRegisteredSimulations()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var parameters = new Dictionary<string, object>();

        _tracker.RegisterSimulation(id1, SimulationType.Cpu, parameters, new CancellationTokenSource());
        _tracker.RegisterSimulation(id2, SimulationType.Memory, parameters, new CancellationTokenSource());
        _tracker.RegisterSimulation(id3, SimulationType.ThreadBlock, parameters, new CancellationTokenSource());

        // Act
        var simulations = _tracker.GetActiveSimulations();

        // Assert
        Assert.Equal(3, simulations.Count);
        Assert.Contains(simulations, s => s.Id == id1 && s.Type == SimulationType.Cpu);
        Assert.Contains(simulations, s => s.Id == id2 && s.Type == SimulationType.Memory);
        Assert.Contains(simulations, s => s.Id == id3 && s.Type == SimulationType.ThreadBlock);
    }

    [Fact]
    public void GetActiveCountByType_ReturnsCorrectCounts()
    {
        // Arrange
        var parameters = new Dictionary<string, object>();
        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Cpu, parameters, new CancellationTokenSource());
        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Cpu, parameters, new CancellationTokenSource());
        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Memory, parameters, new CancellationTokenSource());

        // Act & Assert
        Assert.Equal(2, _tracker.GetActiveCountByType(SimulationType.Cpu));
        Assert.Equal(1, _tracker.GetActiveCountByType(SimulationType.Memory));
        Assert.Equal(0, _tracker.GetActiveCountByType(SimulationType.ThreadBlock));
    }

    [Fact]
    public void CancelAll_CancelsAllSimulationsAndReturnsCount()
    {
        // Arrange
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var parameters = new Dictionary<string, object>();

        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Cpu, parameters, cts1);
        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Memory, parameters, cts2);

        // Act
        var cancelledCount = _tracker.CancelAll();

        // Assert
        Assert.Equal(2, cancelledCount);
        Assert.Equal(0, _tracker.ActiveCount);
        Assert.True(cts1.IsCancellationRequested);
        Assert.True(cts2.IsCancellationRequested);
    }

    [Fact]
    public void CancelAll_WithNoSimulations_ReturnsZero()
    {
        // Act
        var cancelledCount = _tracker.CancelAll();

        // Assert
        Assert.Equal(0, cancelledCount);
    }

    [Fact]
    public void TryGetSimulation_WhenExists_ReturnsTrueAndInfo()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters = new Dictionary<string, object> { ["Test"] = "Value" };
        _tracker.RegisterSimulation(simulationId, SimulationType.Memory, parameters, new CancellationTokenSource());

        // Act
        var found = _tracker.TryGetSimulation(simulationId, out var info);

        // Assert
        Assert.True(found);
        Assert.NotNull(info);
        Assert.Equal(simulationId, info.Id);
        Assert.Equal(SimulationType.Memory, info.Type);
        Assert.Equal("Value", info.Parameters["Test"]);
    }

    [Fact]
    public void TryGetSimulation_WhenDoesNotExist_ReturnsFalseAndNull()
    {
        // Arrange
        var simulationId = Guid.NewGuid();

        // Act
        var found = _tracker.TryGetSimulation(simulationId, out var info);

        // Assert
        Assert.False(found);
        Assert.Null(info);
    }

    [Fact]
    public void RegisterSimulation_SetsCorrectStartedAt()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>();
        var beforeRegistration = DateTimeOffset.UtcNow;

        // Act
        _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters, new CancellationTokenSource());
        var afterRegistration = DateTimeOffset.UtcNow;

        // Assert
        _tracker.TryGetSimulation(simulationId, out var info);
        Assert.NotNull(info);
        Assert.True(info.StartedAt >= beforeRegistration);
        Assert.True(info.StartedAt <= afterRegistration);
    }

    [Fact]
    public void RegisterSimulation_WithNullParameters_ThrowsArgumentNullException()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var cts = new CancellationTokenSource();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, null!, cts));
    }

    [Fact]
    public void RegisterSimulation_WithNullCancellationSource_ThrowsArgumentNullException()
    {
        // Arrange
        var simulationId = Guid.NewGuid();
        var parameters = new Dictionary<string, object>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _tracker.RegisterSimulation(simulationId, SimulationType.Cpu, parameters, null!));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SimulationTracker(null!));
    }

    [Fact]
    public void GetActiveSimulations_ReturnsReadOnlySnapshot()
    {
        // Arrange
        var parameters = new Dictionary<string, object>();
        _tracker.RegisterSimulation(Guid.NewGuid(), SimulationType.Cpu, parameters, new CancellationTokenSource());

        // Act
        var simulations = _tracker.GetActiveSimulations();

        // Assert - The returned list should be read-only
        Assert.IsAssignableFrom<IReadOnlyList<ActiveSimulationInfo>>(simulations);
    }
}
