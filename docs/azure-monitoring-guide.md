# Azure Monitoring Guide for Performance Problem Simulator

This guide explains how to use Azure monitoring tools to diagnose the performance problems created by this simulator.

## 📊 Overview

The Performance Problem Simulator creates four types of issues:

1. **High CPU** - Parallel spin loops consuming all cores
2. **Memory Pressure** - Pinned allocations on the Large Object Heap
3. **Thread Pool Starvation** - Sync-over-async blocking patterns
4. **Slow Requests** - Long-running requests using sync-over-async for CLR Profiler analysis

Each issue can be diagnosed using specific Azure tools and techniques.

---

## 🔥 Diagnosing High CPU

### Symptoms

- Application becomes unresponsive
- Request timeouts increase
- CPU metric shows sustained high usage (>80%)

### Azure Tools to Use

#### 1. App Service Diagnose and Solve Problems

1. Navigate to your App Service in the Azure Portal
2. Click **Diagnose and solve problems** in the left menu
3. Select **Availability and Performance** > **High CPU Analysis**
4. Review the timeline and process breakdown

#### 2. Application Insights Live Metrics

1. Open Application Insights for your app
2. Go to **Live Metrics**
3. Observe the CPU graph in real-time
4. Note the correlation with incoming requests

#### 3. CPU Profiling (Advanced)

1. In App Service, go to **Diagnose and solve problems**
2. Search for "CPU Profiling"
3. Click **Collect Profiler Trace**
4. Trigger the CPU simulation
5. Wait for collection to complete
6. Download and analyze the `.diagsession` file

### What to Look For

```
In the profiler trace, you'll see:
- Parallel.For loops
- SpinWait operations
- High self-time in CpuStressService.RunStressLoop
```

### Code Pattern (What's Wrong)

```csharp
// This is the intentional "bad" code in CpuStressService:
Parallel.For(0, Environment.ProcessorCount, _ =>
{
    while (!cancellationToken.IsCancellationRequested)
    {
        // Intentionally consuming CPU with no useful work
        Thread.SpinWait(10000);
    }
});
```

---

## 📊 Diagnosing Memory Pressure

### Symptoms

- Increasing memory usage over time
- GC collection frequency increases
- Application may become slower due to GC pauses
- Eventually, OutOfMemoryException

### Azure Tools to Use

#### 1. App Service Metrics

1. Navigate to your App Service
2. Go to **Metrics**
3. Add metrics: **Memory working set**, **Private Bytes**
4. Set time granularity to 1 minute
5. Observe memory growth during simulation

#### 2. Memory Dump Analysis

1. Go to **Diagnose and solve problems**
2. Search for "Memory Dump"
3. Click **Collect Memory Dump**
4. Choose "Full dump" for detailed analysis
5. Download and open in Visual Studio or WinDbg

#### 3. Application Insights

1. Open Application Insights
2. Go to **Performance** > **Dependencies**
3. Look for slow GC-related patterns

### What to Look For

```
In a memory dump, look for:
- Large byte[] arrays in the Large Object Heap (LOH)
- GCHandle.Alloc pinned objects
- Objects not being collected due to pinning
```

### Code Pattern (What's Wrong)

```csharp
// This is the intentional "bad" code in MemoryPressureService:
var data = new byte[sizeMegabytes * 1024 * 1024];
var pinnedHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

// The pinned allocation:
// 1. Goes directly to the Large Object Heap (LOH) since > 85KB
// 2. Cannot be moved by GC, causing fragmentation
// 3. Remains in memory until explicitly released
```

---

## 🧵 Diagnosing Thread Pool Starvation

### Symptoms

- Requests queue up and timeout
- Latency spikes even for simple requests
- Thread pool exhaustion warnings in logs
- Health checks may fail

### Azure Tools to Use

#### 1. Application Insights Dependencies

1. Open Application Insights
2. Go to **Application Map**
3. Look for high latency on outgoing calls
4. Note the "waiting for thread" pattern

#### 2. App Service Diagnostics

1. Go to **Diagnose and solve problems**
2. Search for "Threading"
3. Review thread count and contention metrics

#### 3. Performance Counters

Monitor these counters:
- `.NET CLR Thread Pool / # of Threads`
- `.NET CLR Thread Pool / Queue Length`
- `Process / Thread Count`

### What to Look For

```
Signs of thread pool starvation:
- Thread count increasing steadily
- Queue length growing
- Long request durations
- Timeouts on simple operations
```

### Code Pattern (What's Wrong)

```csharp
// This is the intentional "bad" code in ThreadBlockService:
Task.Run(async () =>
{
    // BAD: Blocking a thread pool thread with synchronous wait!
    // This is the "sync-over-async" anti-pattern.
    Task.Delay(delayMs, cancellationToken).Wait();
    
    // Problem: Each call to .Wait() blocks a thread pool thread
    // until the delay completes. With enough concurrent requests,
    // the thread pool becomes exhausted and new requests must wait.
});
```

---

## � Diagnosing Slow Requests with CLR Profiler

The Slow Request Simulator generates long-running requests (~25 seconds each) using sync-over-async patterns specifically designed to be easily identifiable in CLR Profiler traces.

### Purpose

This simulation is designed for **training developers to identify blocking calls** in profiler traces. Unlike the Thread Pool Starvation simulation (which causes many short blocks), this creates individual long requests that are clearly visible in call stacks.

### Azure Tools to Use

#### 1. Azure Auto-Heal with Slow Request Trigger

1. Navigate to your App Service in the Azure Portal
2. Go to **Diagnose and solve problems**
3. Search for "Auto-Heal"
4. Configure slow request trigger (e.g., requests > 20 seconds)

#### 2. CLR Profiler / Azure Profiler

1. In App Service, go to **Diagnose and solve problems**
2. Search for "Profiler"
3. Start a **CPU Profiler** trace (60-120 seconds recommended)
4. Trigger the slow request simulation
5. Download and analyze the trace

### What to Look For in CLR Profiler

The service uses three different sync-over-async patterns with **intentionally descriptive method names**:

#### Scenario 1: Simple Blocking
Look for methods: `FetchDataSync_BLOCKING_HERE`, `ProcessDataSync_BLOCKING_HERE`, `SaveDataSync_BLOCKING_HERE`
```
These methods use Thread.Sleep to block - clearly visible in profiler.
In the profiler, you'll see time spent at:
  → SlowRequestService.FetchDataSync_BLOCKING_HERE
    → Thread.Sleep
```

#### Scenario 2: Nested Sync Methods
Look for methods ending in: `_BLOCKS_INTERNALLY`
```
These are synchronous methods that block internally using Thread.Sleep:
  → ValidateOrderSync_BLOCKS_INTERNALLY
  → CheckInventorySync_BLOCKS_INTERNALLY  
  → ProcessPaymentSync_BLOCKS_INTERNALLY
  → SendConfirmationSync_BLOCKS_INTERNALLY
```

#### Scenario 3: Database/HTTP Pattern
Look for methods ending in: `Sync_SYNC_BLOCK`
```
Simulated database and HTTP blocking calls:
  → GetCustomerFromDatabaseSync_SYNC_BLOCK
  → GetOrderHistoryFromDatabaseSync_SYNC_BLOCK
  → CheckInventoryServiceSync_SYNC_BLOCK
  → GetRecommendationsFromMLServiceSync_SYNC_BLOCK
  → BuildResponseSync_SYNC_BLOCK
```

### Recommended Settings for CLR Profiler (60s trace)

| Setting | Value | Reason |
|---------|-------|--------|
| Request Duration | 25s | Long enough to capture in trace |
| Interval | 10s | Multiple requests during 60s trace |
| Max Requests | 6 | Enough samples without overwhelming |

### Code Pattern (What the Simulation Does)

```csharp
// SCENARIO 1: Direct blocking - most obvious in traces
public void FetchDataSync_BLOCKING_HERE(int delayMs)
{
    // Intentional blocking - will show in profiler as Thread.Sleep
    Thread.Sleep(delayMs);
}

// SCENARIO 2: Nested blocking inside sync methods
public void ValidateOrderSync_BLOCKS_INTERNALLY(int delayMs)
{
    // Each method internally blocks using Thread.Sleep
    Thread.Sleep(delayMs);
}

// SCENARIO 3: Simulated database/HTTP pattern
public void GetCustomerFromDatabaseSync_SYNC_BLOCK(int delayMs)
{
    // Simulates a blocking database call
    Thread.Sleep(delayMs);
}
```

### Real-World Equivalents

The Thread.Sleep patterns simulate what you'd see with:
- Synchronous database calls with long query times
- HTTP calls to slow external services
- Sync-over-async anti-patterns (Task.Wait(), .Result, GetAwaiter().GetResult())

### Key Insight

The slow request simulator makes blocking **visible in profiler call stacks** because:
1. Requests are long enough (25s) to be captured during any reasonable trace period
2. Method names explicitly indicate where blocking occurs (e.g., `_BLOCKING_HERE`, `_SYNC_BLOCK`)
3. Multiple scenarios show different blocking patterns commonly found in production code
4. Thread.Sleep shows as clear self-time in the profiler, making it easy to identify

---

## �💥 Diagnosing Application Crashes

### Symptoms

- Application suddenly becomes unavailable
- HTTP 502/503 errors returned to clients
- Azure auto-restarts the application
- Event logs show process termination

### Azure Crash Monitoring

Azure App Service includes built-in Crash Monitoring that can automatically capture memory dumps when your application crashes.

#### Enabling Crash Monitoring

1. Navigate to your App Service in the Azure Portal
2. Go to **Diagnose and solve problems**
3. Search for "Crash Monitoring"
4. Enable crash dump collection

> **⚠️ Important: Storage Account Restrictions**
>
> In some Azure environments, Crash Monitoring may not work due to security restrictions on storage accounts. If you cannot use Crash Monitoring, you can collect memory dumps manually using **ProcDump** from the Kudu console.

### Using ProcDump for Manual Dump Collection

[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump) is a Sysinternals command-line utility that can monitor applications and generate crash dumps. It's available on Azure App Service through the Kudu console.

#### Accessing the Kudu Console

1. Navigate to your App Service
2. Go to **Development Tools** > **Advanced Tools**
3. Click **Go** to open Kudu
4. Select **Debug console** > **CMD** or **PowerShell**

#### Common ProcDump Commands

```bash
# Write a full memory dump of a process by name
procdump -ma w3wp.exe

# Write a full dump when an unhandled exception occurs
procdump -ma -e w3wp.exe

# Write a full dump on 1st or 2nd chance exception
procdump -ma -e 1 w3wp.exe

# Write up to 10 dumps, one per exception
procdump -ma -n 10 -e 1 w3wp.exe

# Write a dump when CPU exceeds 80% for 10 seconds
procdump -ma -c 80 -s 10 w3wp.exe

# Write a dump when memory exceeds 1GB
procdump -ma -m 1024 w3wp.exe

# Write a dump when a hung window is detected
procdump -ma -h w3wp.exe
```

#### ProcDump Options Reference

| Option | Description |
|--------|-------------|
| `-ma` | Full dump (all memory) |
| `-mm` | Mini dump (default, smaller size) |
| `-mp` | MiniPlus dump (detailed but 10-75% smaller than full) |
| `-e` | Dump on unhandled exception (add `1` for first-chance) |
| `-c` | CPU threshold percentage |
| `-m` | Memory commit threshold in MB |
| `-n` | Number of dumps to write before exiting |
| `-s` | Consecutive seconds before dump (default 10) |
| `-h` | Dump on hung window |
| `-t` | Dump on process termination |

#### Downloading the Dump File

1. In Kudu, navigate to the folder where the dump was created
2. Click the download icon next to the `.dmp` file
3. Open the dump in Visual Studio, WinDbg, or another debugger

---

## ⏱️ Using the Request Latency Monitor

The dashboard includes a **Request Latency Monitor** that demonstrates how thread pool starvation affects request processing time.

### How It Works

1. A dedicated background thread (not from the thread pool) continuously sends probe requests to `/api/health/probe`
2. The probe endpoint is lightweight - it simply returns a timestamp
3. Latency is measured from request start to response received
4. Results are broadcast via SignalR to the dashboard

### What to Observe

| Scenario | Expected Latency | What's Happening |
|----------|-----------------|------------------|
| Normal operation | < 50ms | Thread pool threads are available |
| Thread pool starvation | 100ms - 30s+ | Requests queued waiting for threads |
| Timeout | 30s | No thread available within timeout |

### Key Insight

The probe runs on a **dedicated thread** (not from the thread pool), so it can always send requests. However, the ASP.NET Core server uses the thread pool to process incoming requests. During starvation:

1. The probe request is sent immediately
2. The request sits in the ASP.NET Core queue
3. It waits for a thread pool thread to become available
4. Latency = time spent waiting + processing time

This directly demonstrates how sync-over-async anti-patterns impact end-user response times.

### Correlating with Azure Metrics

Compare the dashboard's latency chart with:

- **Application Insights** > Live Metrics > Request Duration
- **App Service Metrics** > Response Time
- **Thread Pool** section showing blocked threads

---

## 🛠️ Recommended Monitoring Setup

### 1. Enable Application Insights

```bash
# Via Azure CLI
az monitor app-insights component create \
  --app ai-perf-simulator \
  --location eastus \
  --resource-group rg-perf-simulator \
  --application-type web

# Get instrumentation key
az monitor app-insights component show \
  --app ai-perf-simulator \
  --resource-group rg-perf-simulator \
  --query instrumentationKey -o tsv
```

### 2. Configure Alerts

Create alerts for:

| Metric | Threshold | Action |
|--------|-----------|--------|
| CPU Percentage | > 80% for 5 min | Email |
| Memory Working Set | > 1 GB | Email |
| Response Time | > 5s average | Email |
| Failed Requests | > 10/minute | Email + PagerDuty |

### 3. Enable Diagnostic Logs

```bash
az webapp log config \
  --resource-group rg-perf-simulator \
  --name your-app-name \
  --application-logging filesystem \
  --detailed-error-messages true \
  --failed-request-tracing true \
  --web-server-logging filesystem
```

---

## 📈 Best Practices

### DO:
- ✅ Use Application Insights for production apps
- ✅ Set up alerts before issues occur
- ✅ Enable diagnostic logging proactively
- ✅ Monitor thread pool metrics
- ✅ Use async/await properly throughout

### DON'T:
- ❌ Use `.Wait()` or `.Result` on async methods
- ❌ Block thread pool threads
- ❌ Allocate large pinned arrays without releasing
- ❌ Use spin loops for delays
- ❌ Ignore memory growth over time

---

## 🔗 Additional Resources

- [Troubleshoot performance issues - Azure App Service](https://docs.microsoft.com/en-us/azure/app-service/troubleshoot-performance-issues)
- [.NET memory management](https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/)
- [Async/Await Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Thread pool starvation](https://docs.microsoft.com/en-us/archive/blogs/vancem/diagnosing-net-core-threadpool-starvation-with-perfview-why-my-service-is-not-saturating-all-cores-or-seems-to-stall)
