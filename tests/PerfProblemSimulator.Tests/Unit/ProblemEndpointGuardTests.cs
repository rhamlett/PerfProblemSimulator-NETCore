using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PerfProblemSimulator.Middleware;
using System.Text.Json;

namespace PerfProblemSimulator.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="ProblemEndpointGuard"/> middleware.
/// </summary>
/// <remarks>
/// These tests verify that the middleware correctly blocks or allows requests
/// based on the DISABLE_PROBLEM_ENDPOINTS environment variable.
/// </remarks>
public class ProblemEndpointGuardTests : IDisposable
{
    private readonly Mock<ILogger<ProblemEndpointGuard>> _loggerMock;
    private readonly string? _originalEnvValue;

    public ProblemEndpointGuardTests()
    {
        _loggerMock = new Mock<ILogger<ProblemEndpointGuard>>();
        // Save original environment variable value to restore after tests
        _originalEnvValue = Environment.GetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar);
    }

    public void Dispose()
    {
        // Restore original environment variable value
        if (_originalEnvValue == null)
        {
            Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, null);
        }
        else
        {
            Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, _originalEnvValue);
        }
    }

    [Theory]
    [InlineData("/api/trigger-high-cpu")]
    [InlineData("/api/trigger-sync-over-async")]
    [InlineData("/api/allocate-memory")]
    [InlineData("/api/release-memory")]
    public async Task InvokeAsync_WhenEndpointsDisabled_BlocksGuardedPaths(string path)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, "true");
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext(path);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled, "Next middleware should not be called for blocked endpoints");
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/metrics")]
    [InlineData("/api/admin/stats")]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task InvokeAsync_WhenEndpointsDisabled_AllowsNonGuardedPaths(string path)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, "true");
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext(path);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled, $"Next middleware should be called for non-guarded path: {path}");
    }

    [Theory]
    [InlineData("/api/trigger-high-cpu")]
    [InlineData("/api/allocate-memory")]
    [InlineData("/api/health")]
    public async Task InvokeAsync_WhenEndpointsEnabled_AllowsAllPaths(string path)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, "false");
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext(path);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled, $"Next middleware should be called when endpoints are enabled: {path}");
    }

    [Fact]
    public async Task InvokeAsync_WhenEnvVarNotSet_AllowsGuardedPaths()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, null);
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext("/api/trigger-high-cpu");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled, "Next middleware should be called when env var is not set");
    }

    [Fact]
    public async Task InvokeAsync_WhenBlocked_ReturnsCorrectErrorResponse()
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, "true");
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext("/api/trigger-high-cpu");
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var responseContent = await reader.ReadToEndAsync();

        var response = JsonSerializer.Deserialize<JsonElement>(responseContent);
        Assert.Equal("ENDPOINT_DISABLED", response.GetProperty("error").GetString());
        Assert.Contains("DISABLE_PROBLEM_ENDPOINTS", response.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("true")]
    public async Task InvokeAsync_WithDifferentCasings_TreatsAsTrueCorrectly(string envValue)
    {
        // Arrange
        Environment.SetEnvironmentVariable(ProblemEndpointGuard.DisableEndpointsEnvVar, envValue);
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ProblemEndpointGuard(next, _loggerMock.Object);
        var context = CreateHttpContext("/api/trigger-high-cpu");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled, $"Endpoints should be blocked when env var is '{envValue}'");
    }

    [Fact]
    public void Constructor_WithNullNext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProblemEndpointGuard(null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProblemEndpointGuard(next, null!));
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
