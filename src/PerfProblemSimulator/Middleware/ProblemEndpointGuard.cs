using PerfProblemSimulator.Models;
using System.Text.Json;

namespace PerfProblemSimulator.Middleware;

/// <summary>
/// Middleware that guards problem simulation endpoints.
/// When DISABLE_PROBLEM_ENDPOINTS environment variable is set to "true",
/// all requests to /api/trigger-*, /api/allocate-*, and /api/release-* endpoints are blocked.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This middleware implements a safety mechanism that allows
/// the application to be deployed in production-like environments without the risk of
/// accidentally triggering performance problems. This is a common pattern for feature flags
/// and kill switches in production systems.
/// </para>
/// <para>
/// The middleware checks the environment variable at request time, not at startup, which
/// allows for dynamic enabling/disabling without restarting the application. However,
/// for most deployment scenarios, the value would be set at deployment time.
/// </para>
/// <para>
/// <strong>Azure App Service Note:</strong> In Azure App Service, you can set this
/// environment variable in the Configuration blade under Application Settings.
/// </para>
/// </remarks>
public class ProblemEndpointGuard
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemEndpointGuard> _logger;

    /// <summary>
    /// The environment variable name that controls whether problem endpoints are disabled.
    /// </summary>
    public const string DisableEndpointsEnvVar = "DISABLE_PROBLEM_ENDPOINTS";

    /// <summary>
    /// Path prefixes that are subject to the guard.
    /// </summary>
    private static readonly string[] GuardedPathPrefixes =
    [
        "/api/trigger-",
        "/api/allocate-",
        "/api/release-"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="ProblemEndpointGuard"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording guard decisions.</param>
    public ProblemEndpointGuard(RequestDelegate next, ILogger<ProblemEndpointGuard> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes an HTTP request, blocking it if problem endpoints are disabled.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is a guarded endpoint
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var isGuardedEndpoint = GuardedPathPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (isGuardedEndpoint && AreEndpointsDisabled())
        {
            _logger.LogWarning(
                "Blocked request to guarded endpoint {Path} because {EnvVar} is set to true",
                path,
                DisableEndpointsEnvVar);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var errorResponse = ErrorResponse.EndpointDisabled();
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, jsonOptions));
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Checks whether problem endpoints are disabled via environment variable.
    /// </summary>
    /// <returns>True if endpoints are disabled, false otherwise.</returns>
    private static bool AreEndpointsDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableEndpointsEnvVar);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extension methods for registering the <see cref="ProblemEndpointGuard"/> middleware.
/// </summary>
public static class ProblemEndpointGuardExtensions
{
    /// <summary>
    /// Adds the problem endpoint guard middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseProblemEndpointGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ProblemEndpointGuard>();
    }
}
