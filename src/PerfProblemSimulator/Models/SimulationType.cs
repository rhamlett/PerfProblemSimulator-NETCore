namespace PerfProblemSimulator.Models;

/// <summary>
/// Represents the type of performance problem being simulated.
/// Each type corresponds to a different anti-pattern that causes specific performance issues.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> These simulation types represent common performance anti-patterns
/// that you might encounter in production applications. Understanding how to identify and diagnose
/// each type is crucial for effective performance troubleshooting.
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Cpu</term>
/// <description>
/// Simulates high CPU usage through spin loops. In real applications, this might be caused by
/// inefficient algorithms, excessive calculations, or infinite loops.
/// </description>
/// </item>
/// <item>
/// <term>Memory</term>
/// <description>
/// Simulates memory pressure by allocating and holding large byte arrays. In real applications,
/// this might be caused by memory leaks, caching too much data, or loading large datasets.
/// </description>
/// </item>
/// <item>
/// <term>ThreadBlock</term>
/// <description>
/// Simulates thread pool starvation through sync-over-async patterns. This is one of the most
/// common causes of application hangs in ASP.NET Core applications.
/// </description>
/// </item>
/// </list>
/// </remarks>
public enum SimulationType
{
    /// <summary>
    /// High CPU usage simulation using parallel spin loops.
    /// Diagnosis tools: Task Manager, dotnet-counters, Application Insights CPU metrics.
    /// </summary>
    Cpu,

    /// <summary>
    /// Memory pressure simulation by allocating and pinning large byte arrays.
    /// Diagnosis tools: dotnet-dump, dotnet-gcdump, Application Insights memory metrics.
    /// </summary>
    Memory,

    /// <summary>
    /// Thread pool starvation simulation using sync-over-async anti-patterns.
    /// Diagnosis tools: ThreadPool.GetAvailableThreads(), dotnet-counters, Application Insights request queuing.
    /// </summary>
    ThreadBlock,

    /// <summary>
    /// Application crash simulation that terminates the process.
    /// Diagnosis tools: Azure Crash Monitoring, Windows Error Reporting, crash dump analysis with WinDbg.
    /// </summary>
    Crash
}
