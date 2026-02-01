using PerfProblemSimulator.Models;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfProblemSimulator.Services;

/// <summary>
/// Service that triggers intentional application crashes for educational purposes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Educational Note:</strong> This service demonstrates various ways an application
/// can crash, which is useful for learning:
/// </para>
/// <list type="bullet">
/// <item>How to configure Azure App Service crash monitoring</item>
/// <item>How to collect and analyze crash dumps</item>
/// <item>How to use Azure Diagnostics to investigate crashes</item>
/// <item>Understanding different types of fatal errors in .NET</item>
/// </list>
/// <para>
/// <strong>Azure Configuration:</strong> To collect crash dumps in Azure App Service:
/// </para>
/// <list type="number">
/// <item>Go to Diagnose and Solve Problems</item>
/// <item>Search for "Crash Monitoring"</item>
/// <item>Enable crash monitoring with memory dump collection</item>
/// <item>Trigger a crash using this service</item>
/// <item>Download and analyze the crash dump</item>
/// </list>
/// </remarks>
public class CrashService : ICrashService
{
    private readonly ILogger<CrashService> _logger;

    public CrashService(ILogger<CrashService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void TriggerCrash(CrashType crashType, int delaySeconds = 0, string? message = null, bool synchronous = false)
    {
        var crashMessage = message ?? $"Intentional {crashType} crash triggered by Performance Problem Simulator";

        _logger.LogCritical(
            "⚠️ CRASH TRIGGERED: Type={CrashType}, Delay={DelaySeconds}s, Synchronous={Synchronous}, Message={Message}",
            crashType, delaySeconds, synchronous, crashMessage);

        // Synchronous mode: crash immediately during the request (best for Azure Crash Monitoring)
        if (synchronous)
        {
            _logger.LogCritical("💥 SYNCHRONOUS CRASH - No response will be sent!");
            ExecuteCrash(crashType, crashMessage);
            return; // Never reached
        }

        // Async mode: crash on background thread after response is sent
        if (delaySeconds > 0)
        {
            _logger.LogWarning("Crash will occur in {DelaySeconds} seconds...", delaySeconds);
            
            // Use a dedicated thread for the crash to ensure it happens even during thread pool starvation
            var crashThread = new Thread(() =>
            {
                Thread.Sleep(delaySeconds * 1000);
                ExecuteCrash(crashType, crashMessage);
            })
            {
                Name = "CrashThread",
                IsBackground = false // Ensure the thread keeps the process alive until crash
            };
            crashThread.Start();
        }
        else
        {
            // Execute immediately on a separate thread so the HTTP response can be sent
            var crashThread = new Thread(() =>
            {
                Thread.Sleep(100); // Small delay to allow response to be sent
                ExecuteCrash(crashType, crashMessage);
            })
            {
                Name = "CrashThread",
                IsBackground = false
            };
            crashThread.Start();
        }
    }

    /// <summary>
    /// Executes the actual crash based on the crash type.
    /// </summary>
    private void ExecuteCrash(CrashType crashType, string message)
    {
        _logger.LogCritical("💥 Executing {CrashType} crash NOW!", crashType);

        switch (crashType)
        {
            case CrashType.FailFast:
                ExecuteFailFast(message);
                break;

            case CrashType.StackOverflow:
                ExecuteStackOverflow(0);
                break;

            case CrashType.UnhandledException:
                ExecuteUnhandledException(message);
                break;

            case CrashType.AccessViolation:
                ExecuteAccessViolation();
                break;

            case CrashType.OutOfMemory:
                ExecuteOutOfMemory();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(crashType), crashType, "Unknown crash type");
        }
    }

    /// <summary>
    /// Triggers Environment.FailFast which immediately terminates the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> Environment.FailFast is designed for situations
    /// where continuing execution could cause data corruption or security issues.
    /// It bypasses all finalizers and exception handlers.
    /// </para>
    /// <para>
    /// On Windows, this creates a Windows Error Report that can be captured by
    /// Azure crash monitoring. The crash dump will show the exact call stack
    /// and the custom message provided.
    /// </para>
    /// </remarks>
    private void ExecuteFailFast(string message)
    {
        // Create an exception to include in the crash dump for additional context
        var exception = new InvalidOperationException(
            "This exception is intentionally included in the FailFast crash dump for analysis.");
        
        Environment.FailFast(message, exception);
    }

    /// <summary>
    /// Triggers a StackOverflowException via infinite recursion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> StackOverflowException cannot be caught in .NET.
    /// When the stack overflows, the CLR terminates the process immediately.
    /// </para>
    /// <para>
    /// In the crash dump, you'll see a very deep call stack with repeated calls
    /// to this method. This is a classic example used to teach stack analysis.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)] // Prevent tail-call optimization
    private void ExecuteStackOverflow(int depth)
    {
        // Create some local variables to consume stack space faster
        var buffer = new byte[1024];
        buffer[0] = (byte)(depth % 256);
        
        // Infinite recursion
        ExecuteStackOverflow(depth + 1);
        
        // This line is never reached but prevents tail-call optimization
        GC.KeepAlive(buffer);
    }

    /// <summary>
    /// Throws an unhandled exception on a background thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> In .NET 6+, unhandled exceptions on thread pool
    /// threads terminate the process by default. This demonstrates proper exception
    /// handling importance.
    /// </para>
    /// </remarks>
    private void ExecuteUnhandledException(string message)
    {
        // Create a new thread (not thread pool) that throws an unhandled exception
        var crashThread = new Thread(() =>
        {
            throw new ApplicationException(
                $"Intentional unhandled exception: {message}\n\n" +
                "This exception was thrown on a background thread without a try-catch handler. " +
                "In .NET, unhandled exceptions on threads terminate the process.");
        })
        {
            Name = "UnhandledExceptionThread",
            IsBackground = false
        };
        
        crashThread.Start();
        crashThread.Join(); // Wait for the crash
    }

    /// <summary>
    /// Triggers an access violation by writing to invalid memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> Access violations occur when code attempts to
    /// read or write memory it doesn't have permission to access. This is common in:
    /// </para>
    /// <list type="bullet">
    /// <item>Native code interop (P/Invoke) bugs</item>
    /// <item>Unsafe code with bad pointer arithmetic</item>
    /// <item>Buffer overflows</item>
    /// </list>
    /// <para>
    /// The crash dump will show an AccessViolationException with the memory address
    /// that was accessed illegally.
    /// </para>
    /// </remarks>
    private unsafe void ExecuteAccessViolation()
    {
        // Write to address 0 (null pointer), which is always invalid
        int* ptr = (int*)0;
        *ptr = 42; // This will cause an access violation
    }

    /// <summary>
    /// Allocates memory until the process runs out and crashes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Educational Note:</strong> Unlike the memory pressure simulation which
    /// allocates controlled amounts, this continues allocating until the process crashes.
    /// </para>
    /// <para>
    /// The crash dump will show massive memory allocations and can be used to learn
    /// about memory analysis tools like WinDbg's !dumpheap command.
    /// </para>
    /// </remarks>
    private void ExecuteOutOfMemory()
    {
        // Use a static/class-level list to ensure GC cannot reclaim allocations
        // even if an exception occurs
        var allocations = new List<byte[]>();
        var allocationSize = 100 * 1024 * 1024; // 100 MB chunks
        var totalAllocated = 0L;

        _logger.LogWarning("Starting aggressive memory allocation until crash...");

        // Disable GC compaction to make it harder for the runtime to recover
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.Default;

        try
        {
            while (true)
            {
                // Allocate pinned memory to prevent GC from moving or reclaiming it
                var block = GC.AllocateArray<byte>(allocationSize, pinned: true);
                
                // Touch the memory to ensure it's committed
                for (int i = 0; i < block.Length; i += 4096)
                {
                    block[i] = 0xFF;
                }
                
                allocations.Add(block);
                totalAllocated += allocationSize;
                
                _logger.LogWarning("Allocated {TotalMB} MB (pinned)...", totalAllocated / (1024 * 1024));

                // Prevent GC from running during allocation loop
                GC.TryStartNoGCRegion(allocationSize, disallowFullBlockingGC: false);
            }
        }
        catch (OutOfMemoryException)
        {
            _logger.LogCritical("OutOfMemoryException caught at {TotalMB} MB, forcing fatal crash...", 
                totalAllocated / (1024 * 1024));
            
            // Keep allocations alive to prevent GC from recovering
            GC.KeepAlive(allocations);

            // Use FailFast to guarantee process termination with a crash dump
            // This is more reliable than trying another allocation which might just throw again
            Environment.FailFast(
                $"Intentional OutOfMemory crash: Allocated {totalAllocated / (1024 * 1024)} MB before running out of memory. " +
                "This crash was triggered by the Performance Problem Simulator to demonstrate OOM conditions.",
                new OutOfMemoryException($"Process exhausted memory after allocating {totalAllocated / (1024 * 1024)} MB"));
        }
        catch (InvalidOperationException)
        {
            // TryStartNoGCRegion can throw if already in a no-GC region - ignore and continue
        }
    }

    /// <inheritdoc />
    public Dictionary<CrashType, string> GetCrashTypeDescriptions()
    {
        return new Dictionary<CrashType, string>
        {
            [CrashType.FailFast] = "Calls Environment.FailFast() which immediately terminates the process with a Windows Error Report. Best for general crash testing.",
            [CrashType.StackOverflow] = "Triggers StackOverflowException via infinite recursion. Cannot be caught. Creates interesting stack traces for analysis.",
            [CrashType.UnhandledException] = "Throws an unhandled exception on a background thread, demonstrating the importance of proper exception handling.",
            [CrashType.AccessViolation] = "Writes to invalid memory (null pointer). Demonstrates native-level crashes common in P/Invoke or unsafe code bugs.",
            [CrashType.OutOfMemory] = "Allocates memory until the process crashes. Useful for learning memory dump analysis techniques."
        };
    }
}
