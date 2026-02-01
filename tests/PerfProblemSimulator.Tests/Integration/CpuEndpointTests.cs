using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the CPU stress endpoints.
/// </summary>
/// <remarks>
/// These tests verify the end-to-end behavior of the CPU stress API,
/// including request validation, response format, and proper error handling.
/// </remarks>
public class CpuEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public CpuEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task TriggerHighCpu_WithValidRequest_ReturnsOkWithSimulationResult()
    {
        // Arrange
        var request = new { DurationSeconds = 1 }; // Very short for testing
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal("Cpu", result.GetProperty("type").GetString());
        Assert.Equal("Started", result.GetProperty("status").GetString());
        Assert.True(result.TryGetProperty("simulationId", out _));
    }

    [Fact]
    public async Task TriggerHighCpu_WithDefaultValues_UsesDefaults()
    {
        // Arrange - Empty request body should use defaults
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        Assert.True(actualParams.TryGetProperty("durationSeconds", out var duration));
        Assert.True(duration.GetInt32() > 0, "Should have a default duration");
    }

    [Fact]
    public async Task TriggerHighCpu_WithLargeDuration_UsesRequestedDuration()
    {
        // Arrange
        var request = new { DurationSeconds = 9999 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        var actualDuration = actualParams.GetProperty("durationSeconds").GetInt32();
        Assert.Equal(9999, actualDuration); // No limits, uses requested value
    }

    [Fact]
    public async Task TriggerHighCpu_ReturnsJsonContentType()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TriggerHighCpu_IncludesProcessorCountInResponse()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        Assert.True(actualParams.TryGetProperty("processorCount", out var procCount));
        Assert.Equal(Environment.ProcessorCount, procCount.GetInt32());
    }

    [Fact]
    public async Task TriggerHighCpu_IncludesEstimatedEndTime()
    {
        // Arrange
        var request = new { DurationSeconds = 5 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("estimatedEndAt", out var endAt));
        Assert.NotEqual(JsonValueKind.Null, endAt.ValueKind);
    }

    [Fact]
    public async Task TriggerHighCpu_IncludesStartTime()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var beforeRequest = DateTimeOffset.UtcNow;

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var startedAt = DateTimeOffset.Parse(result.GetProperty("startedAt").GetString()!);
        var afterRequest = DateTimeOffset.UtcNow;

        Assert.True(startedAt >= beforeRequest.AddSeconds(-1), "StartedAt should be recent");
        Assert.True(startedAt <= afterRequest.AddSeconds(1), "StartedAt should not be in future");
    }

    [Fact]
    public async Task TriggerHighCpu_IncludesEducationalMessage()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrEmpty(message.GetString()));
    }

    [Fact]
    public async Task TriggerHighCpu_WithNegativeDuration_HandlesGracefully()
    {
        // Arrange
        var request = new { DurationSeconds = -10 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert - Should either use default or return bad request
        // The important thing is it doesn't throw an exception
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadRequest,
            "Should handle negative duration gracefully");
    }

    [Fact]
    public async Task TriggerHighCpu_AcceptsJsonContentType()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/cpu/trigger-high-cpu", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
