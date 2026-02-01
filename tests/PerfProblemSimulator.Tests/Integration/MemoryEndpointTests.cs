using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the memory pressure endpoints.
/// </summary>
/// <remarks>
/// These tests verify the end-to-end behavior of the memory allocation and
/// release APIs, including request validation, response format, and proper
/// state management.
/// </remarks>
public class MemoryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public MemoryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task AllocateMemory_WithValidRequest_ReturnsOkWithSimulationResult()
    {
        // Arrange
        var request = new { SizeMegabytes = 10 }; // Small for testing
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal("Memory", result.GetProperty("type").GetString());
        Assert.Equal("Started", result.GetProperty("status").GetString());
        Assert.True(result.TryGetProperty("simulationId", out _));
    }

    [Fact]
    public async Task AllocateMemory_WithDefaultValues_UsesDefaults()
    {
        // Arrange - Empty request body should use defaults
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        Assert.True(actualParams.TryGetProperty("sizeMegabytes", out var size));
        Assert.True(size.GetInt32() > 0, "Should have a default size");
    }

    [Fact]
    public async Task AllocateMemory_ReturnsJsonContentType()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AllocateMemory_IncludesEducationalMessage()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrEmpty(message.GetString()));
    }

    [Fact]
    public async Task AllocateMemory_HasNullEstimatedEndTime()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var estimatedEndAt = result.GetProperty("estimatedEndAt");
        Assert.Equal(JsonValueKind.Null, estimatedEndAt.ValueKind);
    }

    [Fact]
    public async Task ReleaseMemory_ReturnsOkWithResult()
    {
        // Arrange - First allocate some memory
        var allocateContent = new StringContent("{}", Encoding.UTF8, "application/json");
        await _client.PostAsync("/api/memory/allocate-memory", allocateContent);

        // Act - Now release it
        var releaseContent = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/memory/release-memory", releaseContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseMemory_WithForceGc_ReturnsResult()
    {
        // Arrange
        var request = new { ForceGarbageCollection = true };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/release-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseMemory_WhenNothingAllocated_ReturnsZeroCount()
    {
        // Arrange - First release any existing memory
        var cleanupContent = new StringContent("{}", Encoding.UTF8, "application/json");
        await _client.PostAsync("/api/memory/release-memory", cleanupContent);

        // Act - Try to release again
        var response = await _client.PostAsync("/api/memory/release-memory", cleanupContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("releasedBlockCount", out var count));
        Assert.Equal(0, count.GetInt32());
    }

    [Fact]
    public async Task AllocateMemory_WithLargeSize_UsesRequestedSize()
    {
        // Arrange
        var request = new { SizeMegabytes = 9999 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/memory/allocate-memory", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        var actualSize = actualParams.GetProperty("sizeMegabytes").GetInt32();
        Assert.Equal(9999, actualSize); // No limits, uses requested value
    }
}
