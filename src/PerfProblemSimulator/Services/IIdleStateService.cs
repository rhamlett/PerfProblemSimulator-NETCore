namespace PerfProblemSimulator.Services;

/// <summary>
/// Interface for managing application idle state to reduce unnecessary network traffic.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// When the dashboard is not actively being used, the application goes idle and stops
/// sending health probes to reduce unnecessary traffic to the network, AppLens, and
/// Application Insights.
/// </para>
/// <para>
/// <strong>ALGORITHM:</strong>
/// <list type="number">
/// <item>Track last activity timestamp (dashboard connection, load test, simulation)</item>
/// <item>After idle timeout (default 20 minutes), enter idle state</item>
/// <item>Stop sending health probes (both local and frontend)</item>
/// <item>Wake up when dashboard loads or page reloads</item>
/// </list>
/// </para>
/// <para>
/// <strong>CONFIGURATION:</strong>
/// <list type="bullet">
/// <item>IDLE_TIMEOUT_MINUTES environment variable overrides the default 20-minute timeout</item>
/// </list>
/// </para>
/// </remarks>
public interface IIdleStateService
{
    /// <summary>
    /// Event raised when the application transitions to idle state.
    /// </summary>
    event EventHandler? GoingIdle;

    /// <summary>
    /// Event raised when the application wakes up from idle state.
    /// </summary>
    event EventHandler? WakingUp;

    /// <summary>
    /// Gets whether the application is currently in idle state.
    /// </summary>
    bool IsIdle { get; }

    /// <summary>
    /// Gets the configured idle timeout in minutes.
    /// </summary>
    int IdleTimeoutMinutes { get; }

    /// <summary>
    /// Records activity to prevent or reset idle state.
    /// Call this when the dashboard connects, load tests run, or simulations start.
    /// </summary>
    void RecordActivity();

    /// <summary>
    /// Wakes up the application from idle state if it is currently idle.
    /// </summary>
    /// <returns>True if the application was idle and is now awake, false if already active.</returns>
    bool WakeUp();
}
