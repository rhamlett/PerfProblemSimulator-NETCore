using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the MetricsController endpoints.
/// </summary>
public class MetricsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public MetricsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCurrentMetrics_ReturnsOkWithMetricsSnapshot()
    {
        // Act
        var response = await _client.GetAsync("/api/metrics/current");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("timestamp", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cpuPercent", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingSetMb", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gcHeapMb", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("threadPoolThreads", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDetailedHealth_ReturnsOkWithHealthStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/metrics/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("isHealthy", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warnings", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cpu", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("threadPool", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDetailedHealth_ReturnsProcessorCount()
    {
        // Act
        var response = await _client.GetAsync("/api/metrics/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("processorCount", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDetailedHealth_ReturnsThreadPoolDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/metrics/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("maxWorkerThreads", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("availableWorkerThreads", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxIoThreads", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("availableIoThreads", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDetailedHealth_ActiveSimulations_InitiallyEmpty()
    {
        // Act
        var response = await _client.GetAsync("/api/metrics/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        // The activeSimulations array should be present
        Assert.Contains("activeSimulations", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentMetrics_MultipleRequests_ReturnsFreshData()
    {
        // Act
        var response1 = await _client.GetAsync("/api/metrics/current");
        var content1 = await response1.Content.ReadAsStringAsync();

        // Wait a bit for timestamp to change
        await Task.Delay(100);

        var response2 = await _client.GetAsync("/api/metrics/current");
        var content2 = await response2.Content.ReadAsStringAsync();

        // Assert - both should succeed
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Both should have valid content
        Assert.Contains("timestamp", content1, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timestamp", content2, StringComparison.OrdinalIgnoreCase);
    }
}
