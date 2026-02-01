namespace PerfProblemSimulator.Models;

/// <summary>
/// Request model for triggering application crashes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This request triggers intentional application crashes
/// for learning how to use Azure crash monitoring and memory dump collection.
/// </para>
/// <para>
/// <strong>WARNING:</strong> These operations will terminate the application process!
/// The application will need to be restarted (Azure App Service does this automatically).
/// </para>
/// </remarks>
public class CrashRequest
{
    /// <summary>
    /// The type of crash to trigger.
    /// </summary>
    public CrashType CrashType { get; set; } = CrashType.FailFast;

    /// <summary>
    /// Optional delay in seconds before the crash occurs.
    /// Allows time to observe the application state before crash.
    /// Default: 0 (immediate)
    /// </summary>
    public int DelaySeconds { get; set; } = 0;

    /// <summary>
    /// Custom message to include in the crash (for FailFast and UnhandledException types).
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Types of crashes that can be triggered.
/// </summary>
public enum CrashType
{
    /// <summary>
    /// Calls Environment.FailFast() which immediately terminates the process.
    /// Creates a Windows Error Report and crash dump if configured.
    /// </summary>
    FailFast = 0,

    /// <summary>
    /// Triggers a StackOverflowException via infinite recursion.
    /// Cannot be caught and immediately terminates the process.
    /// Creates interesting stack traces for analysis.
    /// </summary>
    StackOverflow = 1,

    /// <summary>
    /// Throws an unhandled exception on a background thread.
    /// Terminates the process if unhandled exception policy is set to terminate.
    /// </summary>
    UnhandledException = 2,

    /// <summary>
    /// Triggers an access violation by writing to protected memory.
    /// Requires unsafe code and demonstrates native-level crashes.
    /// </summary>
    AccessViolation = 3,

    /// <summary>
    /// Allocates memory until the process runs out and crashes.
    /// Different from memory pressure - this is meant to be fatal.
    /// </summary>
    OutOfMemory = 4
}
