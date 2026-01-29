# Research: Performance Problem Simulator

**Feature**: 001-perf-problem-simulator  
**Date**: 2026-01-29  
**Purpose**: Resolve technical unknowns and document best practices for implementation

## Research Summary

| Topic | Decision | Confidence |
|-------|----------|------------|
| CPU Stress Technique | `Parallel.For` with `while(true)` spin loops | High |
| Memory Allocation | Byte arrays in static `List<byte[]>` with `GC.AllocateArray(pinned: true)` | High |
| Sync-over-Async Pattern | `Task.Delay(ms).Result` in controller actions | High |
| Metrics Collection | Dedicated `Thread` (not `Task.Run`) with `Process`/`GC`/`ThreadPool` APIs | High |
| Real-time Updates | SignalR with fire-and-forget broadcast from dedicated thread | High |

---

## 1. CPU Stress Implementation

### Decision
Use `Parallel.For` with `Environment.ProcessorCount` iterations, each running a `while(true)` spin loop controlled by `Stopwatch` duration.

### Rationale
- **Proven pattern**: Microsoft's official [DiagnosticScenarios sample](https://github.com/dotnet/samples/tree/main/core/diagnostics/DiagnosticScenarios) uses this exact approach for Azure diagnostics tutorials
- **Multi-core saturation**: Single-threaded spin only shows `100/N%` CPU on N-core machines; `Parallel.For` achieves "near 100%" per acceptance criteria (SC-001)
- **Predictable duration**: `Stopwatch` provides millisecond accuracy without JIT optimization interference
- **Cancellation support**: `CancellationToken` enables graceful shutdown

### Code Pattern
```csharp
public void SpinCpu(int durationMs, CancellationToken cancellationToken)
{
    var endTime = Stopwatch.GetTimestamp() + (durationMs * Stopwatch.Frequency / 1000);
    
    Parallel.For(0, Environment.ProcessorCount, 
        new ParallelOptions { CancellationToken = cancellationToken },
        _ =>
        {
            while (Stopwatch.GetTimestamp() < endTime && !cancellationToken.IsCancellationRequested)
            {
                // Intentionally empty - spin loop consumes CPU
            }
        });
}
```

### Alternatives Rejected
| Alternative | Reason |
|-------------|--------|
| `Thread.SpinWait()` | Less controllable, doesn't scale to all cores easily |
| Mathematical calculations | JIT can optimize away; no diagnostic benefit |
| `Process.ProcessorAffinity` | Platform-specific; doesn't work on Linux/containers |

---

## 2. Memory Pressure Implementation

### Decision
Allocate memory as byte arrays stored in a static `List<byte[]>`, using `GC.AllocateArray<byte>(size, pinned: true)` for pinned allocations that won't be collected or moved.

### Rationale
- **Strong references**: Static collection prevents GC collection until explicitly cleared
- **Pinned arrays**: `GC.AllocateArray` with `pinned: true` is the modern .NET API (no `GCHandle` needed)
- **Large Object Heap**: Chunks >85KB go to LOH, visible in Azure memory metrics
- **Chunk-based allocation**: 1MB chunks allow graceful stopping before OOM

### Code Pattern
```csharp
private static readonly List<byte[]> _allocatedBlocks = new();
private static long _totalAllocatedBytes = 0;

public long Allocate(long requestedBytes)
{
    const int chunkSize = 1024 * 1024; // 1MB chunks
    long allocated = 0;
    
    try
    {
        while (allocated < requestedBytes)
        {
            // Safety check: stop at 90% memory pressure
            var memInfo = GC.GetGCMemoryInfo();
            if (memInfo.MemoryLoadBytes > memInfo.HighMemoryLoadThresholdBytes * 0.9)
                break;
                
            var block = GC.AllocateArray<byte>(chunkSize, pinned: true);
            _allocatedBlocks.Add(block);
            allocated += chunkSize;
        }
        
        Interlocked.Add(ref _totalAllocatedBytes, allocated);
        return allocated;
    }
    catch (OutOfMemoryException)
    {
        return allocated; // Return what we managed to allocate
    }
}

public void Release()
{
    _allocatedBlocks.Clear();
    Interlocked.Exchange(ref _totalAllocatedBytes, 0);
    GC.Collect(2, GCCollectionMode.Forced);
}
```

### Alternatives Rejected
| Alternative | Reason |
|-------------|--------|
| `GCHandle.Alloc` | More complex, requires manual `Free()` |
| Single large array | One OOM crashes everything |
| Unmanaged memory | Doesn't show in managed memory metrics |
| `ArrayPool<byte>` | Designed to reduce memory usage, not increase it |

---

## 3. Sync-over-Async Thread Pool Starvation

### Decision
Use `Task.Delay(ms).Result` in controller actions to block ThreadPool threads, demonstrating the classic sync-over-async anti-pattern.

### Rationale
- **Realistic pattern**: `.Result` and `.GetAwaiter().GetResult()` are the most common real-world anti-patterns
- **Controllable**: Delay duration and concurrent requests control starvation severity
- **Observable**: Thread pool injection rate (~1-2 threads/second) makes symptoms visible over 30-60 seconds
- **No external dependencies**: `Task.Delay` doesn't require network or database

### Code Pattern
```csharp
[HttpPost("trigger-sync-over-async")]
public ActionResult TriggerSyncOverAsync([FromBody] ThreadBlockRequest request)
{
    // THIS IS INTENTIONALLY BAD CODE - demonstrates sync-over-async anti-pattern
    // The .Result call blocks the current ThreadPool thread while waiting for the Task to complete.
    // Under concurrent load, this exhausts the ThreadPool, causing request queuing and timeouts.
    
    var result = SimulateAsyncWork(request.DelayMs).Result;
    return Ok(result);
}

private async Task<string> SimulateAsyncWork(int delayMs)
{
    await Task.Delay(delayMs); // Simulates I/O-bound operation
    return $"Completed after {delayMs}ms";
}
```

### Symptom Timing
| Metric | Healthy | Starving (125 concurrent requests) |
|--------|---------|-------------------------------------|
| Thread count | ~ProcessorCount | Climbing 50-200+ |
| Queue length | 0-low | Growing rapidly |
| Response latency | ~500ms | 3-15+ seconds |
| Time to visible symptoms | N/A | 30-60 seconds |

### Alternatives Rejected
| Alternative | Reason |
|-------------|--------|
| `Thread.Sleep()` | Doesn't demonstrate async anti-pattern |
| `HttpClient.GetAsync().Result` | Adds network dependency |
| `BlockingCollection.Take()` | Less relatable to common code |

---

## 4. Metrics Collection (Responsive Under Starvation)

### Decision
Use a dedicated `Thread` (not `Task.Run` or `BackgroundService`) with `Thread.Sleep` for intervals. Collect metrics via `Process`, `GC`, and `ThreadPool` static APIs. Store snapshots using `Interlocked.Exchange` for thread-safe access.

### Rationale
- **Thread pool independence**: Dedicated `Thread` runs even when thread pool is exhausted
- **No async**: `Thread.Sleep` doesn't use thread pool (unlike `Task.Delay`)
- **Lock-free reads**: `Interlocked.Exchange` + `volatile` allow API endpoints to read without blocking
- **Low overhead**: Direct API calls, no listener/callback infrastructure

### Code Pattern
```csharp
public class MetricsCollector : IDisposable
{
    private readonly Thread _collectorThread;
    private readonly CancellationTokenSource _cts = new();
    private volatile MetricsSnapshot _currentSnapshot;

    public MetricsCollector()
    {
        _collectorThread = new Thread(CollectionLoop)
        {
            IsBackground = true,
            Name = "MetricsCollector",
            Priority = ThreadPriority.BelowNormal
        };
        _collectorThread.Start();
    }

    private void CollectionLoop()
    {
        var process = Process.GetCurrentProcess();
        var lastCpuTime = process.TotalProcessorTime;
        var lastTimestamp = Stopwatch.GetTimestamp();

        while (!_cts.Token.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            
            process.Refresh();
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now);
            var cpuTime = process.TotalProcessorTime;

            var snapshot = new MetricsSnapshot
            {
                Timestamp = DateTime.UtcNow,
                CpuPercent = CalculateCpuPercent(cpuTime - lastCpuTime, elapsed),
                WorkingSetBytes = process.WorkingSet64,
                GcHeapBytes = GC.GetTotalMemory(false),
                ThreadPoolThreads = ThreadPool.ThreadCount,
                ThreadPoolQueueLength = ThreadPool.PendingWorkItemCount,
                ThreadPoolAvailableWorkers = GetAvailableWorkers()
            };

            Interlocked.Exchange(ref _currentSnapshot, snapshot);
            lastCpuTime = cpuTime;
            lastTimestamp = now;
        }
    }

    public MetricsSnapshot GetCurrentSnapshot() => _currentSnapshot;
}
```

### Metrics APIs Reference
| Metric | API |
|--------|-----|
| CPU Time | `Process.TotalProcessorTime` (calculate delta for percentage) |
| Memory Working Set | `Process.WorkingSet64` |
| GC Heap | `GC.GetTotalMemory(false)` |
| GC Info | `GC.GetGCMemoryInfo()` |
| Thread Pool Count | `ThreadPool.ThreadCount` |
| Queue Length | `ThreadPool.PendingWorkItemCount` |
| Available Threads | `ThreadPool.GetAvailableThreads()` |

### Alternatives Rejected
| Alternative | Reason |
|-------------|--------|
| `BackgroundService` | Uses thread pool, starves with application |
| `System.Threading.Timer` | Callback runs on thread pool |
| `PeriodicTimer` | Async, uses thread pool |
| `EventCounters` | External monitoring; callbacks may be delayed |

---

## 5. SignalR Real-Time Broadcasting

### Decision
Inject `IHubContext<MetricsHub>` into the metrics collector. Broadcast from the dedicated thread using fire-and-forget (`_ = hubContext.Clients.All.SendAsync(...)`).

### Rationale
- **Thread-safe**: Hub context is designed for use outside hub methods
- **Non-blocking**: Fire-and-forget doesn't block the dedicated thread
- **Built-in backpressure**: SignalR manages connection I/O efficiently

### Code Pattern
```csharp
public interface IMetricsClient
{
    Task ReceiveMetrics(MetricsSnapshot snapshot);
}

public class MetricsHub : Hub<IMetricsClient>
{
    // Stateless hub - broadcasting done via IHubContext
}

// In dedicated thread:
private void BroadcastLoop()
{
    while (!_cts.Token.IsCancellationRequested)
    {
        Thread.Sleep(1000);
        var snapshot = _metricsCollector.GetCurrentSnapshot();
        
        // Fire-and-forget: don't await to avoid blocking
        _ = _hubContext.Clients.All.ReceiveMetrics(snapshot);
    }
}
```

### Client-Side Pattern
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/metrics")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveMetrics", (snapshot) => {
    updateDashboard(snapshot);
});

await connection.start();
```

---

## Architecture Summary

```
┌─────────────────────────────────────────────────────────────────┐
│                   Dedicated Thread (not ThreadPool)              │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  while (!cancelled)                                         │ │
│  │    Thread.Sleep(1000)                                       │ │
│  │    snapshot = Collect(Process, GC, ThreadPool APIs)         │ │
│  │    Interlocked.Exchange(ref _current, snapshot)             │ │
│  │    _ = _hubContext.Clients.All.ReceiveMetrics(snapshot)     │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌─────────────────────────┐    ┌─────────────────────────┐
│   GET /api/metrics      │    │   SignalR WebSocket     │
│   Volatile.Read(ref _)  │    │   Dashboard real-time   │
│   (ThreadPool thread)   │    │   updates               │
└─────────────────────────┘    └─────────────────────────┘
```

---

## References

- [Microsoft DiagnosticScenarios Sample](https://github.com/dotnet/samples/tree/main/core/diagnostics/DiagnosticScenarios)
- [ASP.NET Core Performance Best Practices - Avoid sync over async](https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices)
- [ThreadPool Starvation Detection in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-threadpool-starvation)
- [GC.AllocateArray API](https://learn.microsoft.com/en-us/dotnet/api/system.gc.allocatearray)
- [ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
