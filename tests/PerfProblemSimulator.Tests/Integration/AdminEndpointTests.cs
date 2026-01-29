using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the AdminController endpoints.
/// </summary>
public class AdminEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AdminEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ResetAll_ReturnsOk()
    {
        // Act
        var response = await _client.PostAsync("/api/admin/reset-all", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memoryBlocksReleased", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bytesReleased", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetAll_AfterMemoryAllocation_ReleasesMemory()
    {
        // Arrange - Allocate some memory first
        var allocateRequest = new StringContent(
            """{"sizeInMegabytes": 10}""",
            System.Text.Encoding.UTF8,
            "application/json");
        await _client.PostAsync("/api/memory/allocate-memory", allocateRequest);

        // Act
        var response = await _client.PostAsync("/api/admin/reset-all", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("activeSimulationCount", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memoryAllocated", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("threadPool", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("processInfo", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStats_ReturnsThreadPoolDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/stats");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("availableWorkerThreads", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxWorkerThreads", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pendingWorkItems", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStats_ReturnsProcessInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/stats");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("processorCount", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingSetBytes", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("managedHeapBytes", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStats_ReturnsMemoryInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/stats");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("blockCount", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("totalBytes", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("totalMegabytes", content, StringComparison.OrdinalIgnoreCase);
    }
}
