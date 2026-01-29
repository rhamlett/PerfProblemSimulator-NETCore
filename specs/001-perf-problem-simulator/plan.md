# Implementation Plan: Performance Problem Simulator

**Branch**: `001-perf-problem-simulator` | **Date**: 2026-01-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-perf-problem-simulator/spec.md`

## Summary

Build an ASP.NET Core web application that intentionally creates controllable performance problems (high CPU, memory pressure, thread pool starvation) for educational purposes. The application provides API endpoints to trigger each problem type, a real-time SPA dashboard using SignalR for monitoring, and includes comprehensive code comments explaining each anti-pattern. Deployable to Azure App Service on Windows or Linux.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 LTS  
**Primary Dependencies**: ASP.NET Core 8.0, SignalR (real-time dashboard), System.Diagnostics (metrics)  
**Storage**: N/A (in-memory state only; no persistence required)  
**Testing**: xUnit, Moq, Microsoft.AspNetCore.Mvc.Testing (integration tests)  
**Target Platform**: Azure App Service (Windows or Linux); also runs locally on Windows/macOS/Linux  
**Project Type**: Single web application (API + SPA served from same project)  
**Performance Goals**: N/A (intentionally creates performance problems for demonstration)  
**Constraints**: Memory allocation capped at 1 GB; CPU duration capped at 300 seconds; must remain responsive to health endpoints under stress  
**Scale/Scope**: Single-user demonstration tool; not designed for concurrent multi-user load testing

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check (Phase 0 Gate)

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Code Quality First | ✅ PASS | FR-016 requires inline comments explaining anti-patterns; SC-006 requires junior-developer-friendly documentation |
| II. Test-First Development | ✅ PASS | Will follow TDD; integration tests for API endpoints, unit tests for services |
| III. Clean Architecture | ✅ PASS | Separation: Controllers → Services → Models; interfaces for metrics collection |
| IV. Defensive Programming | ✅ PASS | FR-011 enforces parameter limits; edge cases define graceful failure modes |
| V. Simplicity & YAGNI | ⚠️ JUSTIFIED | Dedicated metrics thread (FR-013) adds complexity but is required for responsiveness under thread starvation; see Complexity Tracking |

**Gate Result**: PASS (1 justified complexity)

### Post-Design Check (Phase 1 Re-evaluation)

| Principle | Status | Design Artifacts Verified |
|-----------|--------|---------------------------|
| I. Code Quality First | ✅ PASS | OpenAPI spec includes educational descriptions; data-model.md documents all entities |
| II. Test-First Development | ✅ PASS | Test structure defined in plan (Unit + Integration folders); TDD will be enforced in tasks |
| III. Clean Architecture | ✅ PASS | Controllers/Services/Models separation verified; IMetricsCollector interface defined |
| IV. Defensive Programming | ✅ PASS | Validation rules documented in data-model.md; all endpoints have error responses in OpenAPI |
| V. Simplicity & YAGNI | ✅ PASS | Single project structure; vanilla JS for SPA (no framework overhead); complexity justified |

**Post-Design Gate Result**: PASS ✅

## Project Structure

### Documentation (this feature)

```text
specs/001-perf-problem-simulator/
├── plan.md              # This file
├── research.md          # Phase 0 output ✅
├── data-model.md        # Phase 1 output ✅
├── quickstart.md        # Phase 1 output ✅
├── contracts/           # Phase 1 output (OpenAPI spec) ✅
│   └── openapi.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
└── PerfProblemSimulator/
    ├── PerfProblemSimulator.csproj
    ├── Program.cs                    # Application entry point, service registration
    ├── appsettings.json              # Configuration including limits
    ├── appsettings.Development.json
    │
    ├── Controllers/                  # API endpoints (RPC-style)
    │   ├── CpuController.cs          # POST /api/trigger-high-cpu
    │   ├── MemoryController.cs       # POST /api/allocate-memory, POST /api/release-memory
    │   ├── ThreadBlockController.cs  # POST /api/trigger-sync-over-async
    │   ├── MetricsController.cs      # GET /api/metrics/*
    │   └── AdminController.cs        # POST /api/reset-all
    │
    ├── Services/                     # Business logic
    │   ├── ICpuStressService.cs
    │   ├── CpuStressService.cs       # CPU spinning implementation
    │   ├── IMemoryPressureService.cs
    │   ├── MemoryPressureService.cs  # Memory allocation/release
    │   ├── IThreadBlockService.cs
    │   ├── ThreadBlockService.cs     # Sync-over-async anti-pattern
    │   ├── IMetricsCollector.cs
    │   └── MetricsCollector.cs       # Dedicated thread for metrics (FR-013)
    │
    ├── Models/                       # DTOs and domain models
    │   ├── CpuStressRequest.cs
    │   ├── MemoryAllocationRequest.cs
    │   ├── ThreadBlockRequest.cs
    │   ├── ApplicationHealthStatus.cs
    │   └── SimulationResult.cs
    │
    ├── Hubs/                         # SignalR real-time communication
    │   └── MetricsHub.cs             # WebSocket hub for dashboard updates
    │
    ├── Middleware/                   # Cross-cutting concerns
    │   └── ProblemEndpointGuard.cs   # Checks DISABLE_PROBLEM_ENDPOINTS env var
    │
    └── wwwroot/                      # Static SPA files
        ├── index.html                # Dashboard page with warning banner
        ├── css/
        │   └── dashboard.css
        └── js/
            └── dashboard.js          # SignalR client, metrics display

tests/
└── PerfProblemSimulator.Tests/
    ├── PerfProblemSimulator.Tests.csproj
    │
    ├── Unit/                         # Unit tests
    │   ├── CpuStressServiceTests.cs
    │   ├── MemoryPressureServiceTests.cs
    │   ├── ThreadBlockServiceTests.cs
    │   └── MetricsCollectorTests.cs
    │
    └── Integration/                  # Integration tests
        ├── CpuEndpointTests.cs
        ├── MemoryEndpointTests.cs
        ├── ThreadBlockEndpointTests.cs
        ├── MetricsEndpointTests.cs
        └── SignalRIntegrationTests.cs
```

**Structure Decision**: Single ASP.NET Core project with embedded SPA (wwwroot). This keeps deployment simple (single artifact), aligns with Azure App Service deployment patterns, and avoids the complexity of a separate frontend build pipeline. The SPA is minimal vanilla JavaScript with SignalR client—no framework needed for this scope.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Dedicated metrics thread (FR-013) | Health endpoints must remain responsive even when thread pool is starved by sync-over-async simulation | Relying on the main thread pool would make the dashboard unresponsive during the exact scenario it needs to monitor—defeating the educational purpose |
