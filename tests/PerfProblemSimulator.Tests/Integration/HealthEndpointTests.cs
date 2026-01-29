using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PerfProblemSimulator.Tests.Integration;

/// <summary>
/// Integration tests for the health check endpoints.
/// </summary>
/// <remarks>
/// These tests verify that the health endpoints are accessible and return
/// the expected responses. Integration tests use the actual application
/// pipeline to test end-to-end behavior.
/// </remarks>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.True(content.TryGetProperty("timestamp", out _), "Response should include timestamp");
    }

    [Fact]
    public async Task GetHealth_ReturnsJsonContentType()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetHealthStatus_ReturnsDetailedInformation()
    {
        // Act
        var response = await _client.GetAsync("/api/health/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.True(content.TryGetProperty("activeSimulationCount", out var countProp));
        Assert.Equal(0, countProp.GetInt32());
        Assert.True(content.TryGetProperty("activeSimulations", out var simsProp));
        Assert.Equal(JsonValueKind.Array, simsProp.ValueKind);
    }

    [Fact]
    public async Task GetHealthStatus_WithNoSimulations_ReturnsEmptySimulationsList()
    {
        // Act
        var response = await _client.GetAsync("/api/health/status");

        // Assert
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var simulations = content.GetProperty("activeSimulations");
        Assert.Equal(0, simulations.GetArrayLength());
    }

    [Fact]
    public async Task GetHealth_ResponseIncludesValidTimestamp()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var timestampString = content.GetProperty("timestamp").GetString();
        Assert.NotNull(timestampString);

        var timestamp = DateTimeOffset.Parse(timestampString);
        var now = DateTimeOffset.UtcNow;

        // Timestamp should be within the last minute
        Assert.True(timestamp > now.AddMinutes(-1), "Timestamp should be recent");
        Assert.True(timestamp <= now.AddSeconds(1), "Timestamp should not be in the future");
    }

    [Fact]
    public async Task GetHealth_IsAccessibleWithoutAuthentication()
    {
        // This test verifies that health endpoints don't require authentication
        // which is important for load balancers and health probes

        // Act - Using a fresh client without any auth headers
        var response = await _client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/Health")]
    [InlineData("/api/HEALTH")]
    public async Task GetHealth_IsCaseInsensitive(string path)
    {
        // Act
        var response = await _client.GetAsync(path);

        // Assert - Should return 200 for any case variation
        // Note: ASP.NET Core routing is case-insensitive by default
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
