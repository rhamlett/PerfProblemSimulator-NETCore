# Data Model: Performance Problem Simulator

**Feature**: 001-perf-problem-simulator  
**Date**: 2026-01-29  
**Source**: [spec.md](spec.md) Key Entities section

## Entity Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      Request Models (DTOs)                       │
├─────────────────────────────────────────────────────────────────┤
│  CpuStressRequest        MemoryAllocationRequest                │
│  ThreadBlockRequest      ResetRequest                           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Domain Models (State)                        │
├─────────────────────────────────────────────────────────────────┤
│  AllocatedMemoryBlock    ActiveSimulation                       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Response Models (DTOs)                        │
├─────────────────────────────────────────────────────────────────┤
│  ApplicationHealthStatus   SimulationResult                     │
│  MetricsSnapshot           ErrorResponse                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## Request Models

### CpuStressRequest

Represents a request to trigger high CPU usage.

| Field | Type | Required | Default | Constraints | Description |
|-------|------|----------|---------|-------------|-------------|
| `DurationSeconds` | `int` | No | 30 | Min: 1, Max: 300 | How long to sustain high CPU |

**Validation Rules:**
- Duration must be between 1 and 300 seconds (FR-011)
- Values outside range should return 400 Bad Request with explanation

---

### MemoryAllocationRequest

Represents a request to allocate memory for pressure testing.

| Field | Type | Required | Default | Constraints | Description |
|-------|------|----------|---------|-------------|-------------|
| `SizeMegabytes` | `int` | No | 100 | Min: 10, Max: 1024 | Amount of memory to allocate |

**Validation Rules:**
- Size must be between 10 MB and 1024 MB (1 GB) per FR-011
- Allocation is additive (multiple requests accumulate)
- Values outside range should return 400 Bad Request

---

### ThreadBlockRequest

Represents a request to trigger sync-over-async thread blocking.

| Field | Type | Required | Default | Constraints | Description |
|-------|------|----------|---------|-------------|-------------|
| `DelayMilliseconds` | `int` | No | 1000 | Min: 100, Max: 30000 | How long each thread blocks |
| `ConcurrentRequests` | `int` | No | 10 | Min: 1, Max: 200 | Number of concurrent blocking calls |

**Validation Rules:**
- Delay must be between 100ms and 30 seconds
- Concurrent requests capped at 200 to prevent runaway exhaustion
- Combined with external concurrent calls, this determines starvation severity

---

## Domain Models

### AllocatedMemoryBlock

Represents a chunk of memory intentionally held by the application.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Unique identifier for tracking |
| `SizeBytes` | `long` | Actual size of the allocated block |
| `AllocatedAt` | `DateTimeOffset` | When the block was allocated |
| `Data` | `byte[]` | The actual allocated memory (pinned) |

**Lifecycle:**
1. Created when `/api/allocate-memory` is called
2. Held in static collection (prevents GC)
3. Released when `/api/release-memory` is called
4. After release, GC may collect (timing not guaranteed)

---

### ActiveSimulation

Represents a currently running problem simulation.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Unique identifier |
| `Type` | `SimulationType` | Enum: `Cpu`, `Memory`, `ThreadBlock` |
| `StartedAt` | `DateTimeOffset` | When the simulation started |
| `Parameters` | `Dictionary<string, object>` | Configuration used |
| `CancellationSource` | `CancellationTokenSource` | For stopping the simulation |

**SimulationType Enum:**
```csharp
public enum SimulationType
{
    Cpu,
    Memory,
    ThreadBlock
}
```

---

## Response Models

### SimulationResult

Returned after triggering a simulation.

| Field | Type | Description |
|-------|------|-------------|
| `SimulationId` | `Guid` | Identifier for tracking |
| `Type` | `SimulationType` | What type of problem was triggered |
| `Status` | `string` | `"Started"`, `"Completed"`, `"Failed"` |
| `Message` | `string` | Human-readable description |
| `ActualParameters` | `Dictionary<string, object>` | Parameters used (may differ from request if capped) |
| `StartedAt` | `DateTimeOffset` | When the simulation started |
| `EstimatedEndAt` | `DateTimeOffset?` | When it will complete (null for memory) |

---

### ApplicationHealthStatus

Comprehensive health status for the dashboard and metrics endpoint.

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | When this snapshot was taken |
| `Cpu` | `CpuMetrics` | CPU-related metrics |
| `Memory` | `MemoryMetrics` | Memory-related metrics |
| `ThreadPool` | `ThreadPoolMetrics` | Thread pool statistics |
| `ActiveSimulations` | `List<ActiveSimulationSummary>` | Currently running simulations |
| `IsHealthy` | `bool` | Overall health assessment |
| `Warnings` | `List<string>` | Any warning messages |

**CpuMetrics:**
| Field | Type | Description |
|-------|------|-------------|
| `UsagePercent` | `double` | Current CPU usage (0-100) |
| `ProcessorCount` | `int` | Number of logical processors |

**MemoryMetrics:**
| Field | Type | Description |
|-------|------|-------------|
| `WorkingSetBytes` | `long` | Process working set |
| `GcHeapBytes` | `long` | Managed heap size |
| `AllocatedBlocksCount` | `int` | Number of held memory blocks |
| `AllocatedBlocksTotalBytes` | `long` | Total intentionally allocated |
| `MemoryLoadPercent` | `double` | System memory pressure (0-100) |

**ThreadPoolMetrics:**
| Field | Type | Description |
|-------|------|-------------|
| `ThreadCount` | `int` | Current thread pool thread count |
| `PendingWorkItems` | `long` | Items waiting in queue |
| `CompletedWorkItems` | `long` | Total completed since start |
| `AvailableWorkerThreads` | `int` | Available worker threads |
| `MaxWorkerThreads` | `int` | Maximum worker threads |
| `AvailableIoThreads` | `int` | Available I/O threads |
| `MaxIoThreads` | `int` | Maximum I/O threads |

**ActiveSimulationSummary:**
| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Simulation identifier |
| `Type` | `SimulationType` | Type of simulation |
| `StartedAt` | `DateTimeOffset` | Start time |
| `DurationSeconds` | `int?` | Configured duration (null for memory) |

---

### MetricsSnapshot

Lightweight snapshot for SignalR real-time updates (subset of ApplicationHealthStatus).

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | When captured |
| `CpuPercent` | `double` | CPU usage |
| `WorkingSetMb` | `double` | Working set in MB |
| `GcHeapMb` | `double` | GC heap in MB |
| `ThreadPoolThreads` | `int` | Current thread count |
| `ThreadPoolQueueLength` | `long` | Pending work items |
| `ActiveSimulationCount` | `int` | Number of active simulations |

**Design Note:** This is a `readonly record struct` for efficient, lock-free transmission over SignalR.

---

### ErrorResponse

Standard error response format.

| Field | Type | Description |
|-------|------|-------------|
| `Error` | `string` | Error code (e.g., `"VALIDATION_ERROR"`) |
| `Message` | `string` | Human-readable error message |
| `Details` | `Dictionary<string, string[]>?` | Field-level validation errors |
| `Timestamp` | `DateTimeOffset` | When the error occurred |

---

## Validation Summary

| Model | Field | Rule |
|-------|-------|------|
| CpuStressRequest | DurationSeconds | 1 ≤ value ≤ 300 |
| MemoryAllocationRequest | SizeMegabytes | 10 ≤ value ≤ 1024 |
| ThreadBlockRequest | DelayMilliseconds | 100 ≤ value ≤ 30000 |
| ThreadBlockRequest | ConcurrentRequests | 1 ≤ value ≤ 200 |

All validation failures return HTTP 400 with `ErrorResponse` body.

---

## State Management

```
┌─────────────────────────────────────────────────────────────────┐
│                    In-Memory State (Singleton)                   │
├─────────────────────────────────────────────────────────────────┤
│  List<AllocatedMemoryBlock>    Static, thread-safe              │
│  ConcurrentDictionary<Guid, ActiveSimulation>  Active sims      │
│  volatile MetricsSnapshot      Latest metrics                   │
└─────────────────────────────────────────────────────────────────┘
```

**Design Decisions:**
- No persistence required (state is ephemeral by design)
- All state is process-local (not shared across instances)
- Thread-safe collections for concurrent access
- Singleton lifetime for state services
