using PerfProblemSimulator.Models;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service interface for triggering controlled application crashes for diagnostic training.
/// </summary>
/// <remarks>
/// <para>
/// <strong>PURPOSE:</strong>
/// This service provides controlled crash mechanisms to demonstrate different failure modes
/// and how they appear in monitoring systems like Azure App Service Diagnostics, Application
/// Insights, and log aggregators. Each crash type produces different diagnostic artifacts.
/// </para>
/// <para>
/// <strong>WHY THIS EXISTS:</strong>
/// <list type="bullet">
/// <item>Production crashes are stressful - this lets developers practice diagnosis safely</item>
/// <item>Different crash types (OOM, stack overflow, unhandled exception) look different in logs</item>
/// <item>Azure App Service auto-restart behavior can be observed and understood</item>
/// <item>Helps teams build runbooks for different failure scenarios</item>
/// </list>
/// </para>
/// <para>
/// <strong>CRASH TYPES TO IMPLEMENT:</strong>
/// <list type="bullet">
/// <item>OutOfMemory - Allocate memory until process crashes</item>
/// <item>StackOverflow - Infinite recursion until stack exhausts</item>
/// <item>UnhandledException - Throw exception on thread pool thread (no catch)</item>
/// <item>EnvironmentFailFast - Request immediate process termination from OS</item>
/// <item>AccessViolation - Attempt to write to protected memory (unsafe code)</item>
/// </list>
/// </para>
/// <para>
/// <strong>PORTING TO OTHER LANGUAGES:</strong>
/// <list type="bullet">
/// <item>PHP: exit(1), throw new Exception(), infinite recursion</item>
/// <item>Node.js: process.exit(1), throw new Error(), recursive function</item>
/// <item>Java: System.exit(1), throw new RuntimeException(), recursive method</item>
/// <item>Python: sys.exit(1), raise Exception(), recursive function</item>
/// </list>
/// </para>
/// <para>
/// <strong>RELATED FILES:</strong>
/// <list type="bullet">
/// <item>Services/CrashService.cs - Implementation of each crash type</item>
/// <item>Controllers/CrashController.cs - REST API endpoints</item>
/// <item>Models/CrashRequest.cs - Configuration model</item>
/// </list>
/// </para>
/// </remarks>
public interface ICrashService
{
    /// <summary>
    /// Triggers a crash of the specified type.
    /// </summary>
    /// <param name="crashType">The type of crash to trigger.</param>
    /// <param name="delaySeconds">Optional delay before crash (ignored if synchronous).</param>
    /// <param name="message">Optional message for certain crash types.</param>
    /// <param name="synchronous">If true, crashes immediately (no response sent).</param>
    /// <remarks>
    /// <strong>WARNING:</strong> This method will terminate the application process!
    /// </remarks>
    void TriggerCrash(CrashType crashType, int delaySeconds = 0, string? message = null, bool synchronous = false);

    /// <summary>
    /// Gets a description of what each crash type does.
    /// </summary>
    Dictionary<CrashType, string> GetCrashTypeDescriptions();
}
