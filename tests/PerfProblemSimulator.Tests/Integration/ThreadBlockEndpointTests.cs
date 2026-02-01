using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the thread blocking endpoints.
/// </summary>
public class ThreadBlockEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ThreadBlockEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task TriggerSyncOverAsync_WithValidRequest_ReturnsOkWithSimulationResult()
    {
        // Arrange
        var request = new { DelayMilliseconds = 100, ConcurrentRequests = 1 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal("ThreadBlock", result.GetProperty("type").GetString());
        Assert.Equal("Started", result.GetProperty("status").GetString());
        Assert.True(result.TryGetProperty("simulationId", out _));
    }

    [Fact]
    public async Task TriggerSyncOverAsync_WithDefaultValues_UsesDefaults()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        Assert.True(actualParams.TryGetProperty("delayMilliseconds", out var delay));
        Assert.True(delay.GetInt32() > 0, "Should have a default delay");
        Assert.True(actualParams.TryGetProperty("concurrentRequests", out var concurrent));
        Assert.True(concurrent.GetInt32() > 0, "Should have a default concurrency");
    }

    [Fact]
    public async Task TriggerSyncOverAsync_ReturnsJsonContentType()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TriggerSyncOverAsync_IncludesEducationalMessage()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrEmpty(message.GetString()));
    }

    [Fact]
    public async Task TriggerSyncOverAsync_IncludesEstimatedEndTime()
    {
        // Arrange
        var request = new { DelayMilliseconds = 500, ConcurrentRequests = 2 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(result.TryGetProperty("estimatedEndAt", out var endAt));
        Assert.NotEqual(JsonValueKind.Null, endAt.ValueKind);
    }

    [Fact]
    public async Task TriggerSyncOverAsync_WithLargeDelay_UsesRequestedDelay()
    {
        // Arrange
        var request = new { DelayMilliseconds = 999999 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        var actualDelay = actualParams.GetProperty("delayMilliseconds").GetInt32();
        Assert.Equal(999999, actualDelay); // No limits, uses requested value
    }

    [Fact]
    public async Task TriggerSyncOverAsync_WithLargeConcurrency_UsesRequestedConcurrency()
    {
        // Arrange
        var request = new { ConcurrentRequests = 9999 };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client.PostAsync("/api/threadblock/trigger-sync-over-async", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var actualParams = result.GetProperty("actualParameters");
        var actualConcurrent = actualParams.GetProperty("concurrentRequests").GetInt32();
        Assert.Equal(9999, actualConcurrent); // No limits, uses requested value
    }
}
