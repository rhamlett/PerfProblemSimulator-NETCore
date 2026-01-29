namespace PerfProblemSimulator.Models;

/// <summary>
/// Standard error response format for all API endpoints.
/// Provides structured error information including validation details.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> Consistent error responses make it easier to build
/// client applications and troubleshoot issues. This format follows common REST API patterns
/// and includes enough detail to understand what went wrong without exposing sensitive information.
/// </para>
/// </remarks>
public class ErrorResponse
{
    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    /// <remarks>
    /// Common error codes:
    /// <list type="bullet">
    /// <item><term>VALIDATION_ERROR</term><description>Request parameters failed validation</description></item>
    /// <item><term>SIMULATION_ERROR</term><description>Simulation failed to execute</description></item>
    /// <item><term>LIMIT_EXCEEDED</term><description>Resource limit would be exceeded</description></item>
    /// <item><term>ENDPOINT_DISABLED</term><description>Problem endpoints are disabled via environment variable</description></item>
    /// <item><term>INTERNAL_ERROR</term><description>Unexpected server error</description></item>
    /// </list>
    /// </remarks>
    public required string Error { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Field-level validation errors.
    /// Keys are field names, values are arrays of error messages for that field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Example:</strong> If DurationSeconds is outside the allowed range,
    /// this might contain: <c>{ "DurationSeconds": ["Value must be between 1 and 300 seconds"] }</c>
    /// </para>
    /// </remarks>
    public Dictionary<string, string[]>? Details { get; init; }

    /// <summary>
    /// When this error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a validation error response.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional field-level validation errors.</param>
    /// <returns>An ErrorResponse configured as a validation error.</returns>
    public static ErrorResponse ValidationError(string message, Dictionary<string, string[]>? details = null)
    {
        return new ErrorResponse
        {
            Error = "VALIDATION_ERROR",
            Message = message,
            Details = details
        };
    }

    /// <summary>
    /// Creates a simulation error response.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>An ErrorResponse configured as a simulation error.</returns>
    public static ErrorResponse SimulationError(string message)
    {
        return new ErrorResponse
        {
            Error = "SIMULATION_ERROR",
            Message = message
        };
    }

    /// <summary>
    /// Creates an endpoint disabled error response.
    /// </summary>
    /// <returns>An ErrorResponse indicating problem endpoints are disabled.</returns>
    public static ErrorResponse EndpointDisabled()
    {
        return new ErrorResponse
        {
            Error = "ENDPOINT_DISABLED",
            Message = "Problem simulation endpoints are disabled. Set DISABLE_PROBLEM_ENDPOINTS=false to enable."
        };
    }

    /// <summary>
    /// Creates a limit exceeded error response.
    /// </summary>
    /// <param name="message">Description of which limit was exceeded.</param>
    /// <returns>An ErrorResponse indicating a resource limit would be exceeded.</returns>
    public static ErrorResponse LimitExceeded(string message)
    {
        return new ErrorResponse
        {
            Error = "LIMIT_EXCEEDED",
            Message = message
        };
    }
}
