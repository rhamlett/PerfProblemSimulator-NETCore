# Azure Monitoring Guide for Performance Problem Simulator

This guide explains how to use Azure monitoring tools to diagnose the performance problems created by this simulator.

## 📊 Overview

The Performance Problem Simulator creates three types of issues:

1. **High CPU** - Parallel spin loops consuming all cores
2. **Memory Pressure** - Pinned allocations on the Large Object Heap
3. **Thread Pool Starvation** - Sync-over-async blocking patterns

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
