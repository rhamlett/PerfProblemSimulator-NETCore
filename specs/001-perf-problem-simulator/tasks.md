# Tasks: Performance Problem Simulator

**Input**: Design documents from `/specs/001-perf-problem-simulator/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: Following Constitution Principle II (Test-First Development), tests are included and MUST be written before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Source**: `src/PerfProblemSimulator/`
- **Tests**: `tests/PerfProblemSimulator.Tests/`

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create project structure and configure development environment

- [X] T001 Create .NET 8.0 solution file at repository root: `dotnet new sln -n PerfProblemSimulator`
- [X] T002 Create ASP.NET Core Web API project in src/PerfProblemSimulator/PerfProblemSimulator.csproj with SignalR package
- [X] T003 Create xUnit test project in tests/PerfProblemSimulator.Tests/PerfProblemSimulator.Tests.csproj with Moq and Microsoft.AspNetCore.Mvc.Testing
- [X] T004 [P] Configure appsettings.json with ProblemSimulator section (MaxCpuDurationSeconds, MaxMemoryAllocationMb, etc.) in src/PerfProblemSimulator/appsettings.json
- [X] T005 [P] Configure appsettings.Development.json with development-specific settings in src/PerfProblemSimulator/appsettings.Development.json
- [X] T006 [P] Create .editorconfig for consistent C# code style at repository root
- [X] T007 [P] Create Directory.Build.props with common project properties (nullable, implicit usings) at repository root

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Models (Shared DTOs)

- [X] T008 [P] Create SimulationType enum in src/PerfProblemSimulator/Models/SimulationType.cs
- [X] T009 [P] Create SimulationResult response model in src/PerfProblemSimulator/Models/SimulationResult.cs
- [X] T010 [P] Create ErrorResponse model in src/PerfProblemSimulator/Models/ErrorResponse.cs
- [X] T011 [P] Create configuration options class ProblemSimulatorOptions in src/PerfProblemSimulator/Models/ProblemSimulatorOptions.cs

### Core Infrastructure

- [X] T012 Create Program.cs with service registration, SignalR hub mapping, static files, and CORS in src/PerfProblemSimulator/Program.cs
- [X] T013 Create ProblemEndpointGuard middleware that checks DISABLE_PROBLEM_ENDPOINTS env var in src/PerfProblemSimulator/Middleware/ProblemEndpointGuard.cs
- [X] T014 Create ISimulationTracker interface and SimulationTracker service for tracking active simulations in src/PerfProblemSimulator/Services/SimulationTracker.cs
- [X] T015 Create health check endpoint (GET /api/health) in src/PerfProblemSimulator/Controllers/HealthController.cs

### Tests for Foundational

- [X] T016 [P] Unit test for ProblemEndpointGuard middleware in tests/PerfProblemSimulator.Tests/Unit/ProblemEndpointGuardTests.cs
- [X] T017 [P] Unit test for SimulationTracker service in tests/PerfProblemSimulator.Tests/Unit/SimulationTrackerTests.cs
- [X] T018 Integration test for health endpoint in tests/PerfProblemSimulator.Tests/Integration/HealthEndpointTests.cs

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Trigger High CPU Usage (Priority: P1) 🎯 MVP

**Goal**: Enable users to trigger controllable high CPU usage for monitoring practice

**Independent Test**: Invoke POST /api/trigger-high-cpu and observe CPU metrics spike in dashboard or Task Manager

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T019 [P] [US1] Unit test for CpuStressService (validates duration limits, cancellation) in tests/PerfProblemSimulator.Tests/Unit/CpuStressServiceTests.cs
- [X] T020 [P] [US1] Integration test for POST /api/trigger-high-cpu endpoint in tests/PerfProblemSimulator.Tests/Integration/CpuEndpointTests.cs

### Implementation for User Story 1

- [X] T021 [P] [US1] Create CpuStressRequest model with validation attributes in src/PerfProblemSimulator/Models/CpuStressRequest.cs
- [X] T022 [US1] Create ICpuStressService interface in src/PerfProblemSimulator/Services/ICpuStressService.cs
- [X] T023 [US1] Implement CpuStressService with Parallel.For spin loops (with educational comments explaining the anti-pattern) in src/PerfProblemSimulator/Services/CpuStressService.cs
- [X] T024 [US1] Create CpuController with POST /api/trigger-high-cpu endpoint in src/PerfProblemSimulator/Controllers/CpuController.cs
- [X] T025 [US1] Register ICpuStressService in Program.cs DI container
- [X] T026 [US1] Add request logging for CPU stress operations (FR-010)

**Checkpoint**: User Story 1 complete - CPU stress can be triggered and observed independently

---

## Phase 4: User Story 2 - Trigger High Memory Usage (Priority: P2)

**Goal**: Enable users to allocate and release memory to observe memory pressure patterns

**Independent Test**: Invoke POST /api/allocate-memory and observe Working Set increase; invoke POST /api/release-memory to recover

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T027 [P] [US2] Unit test for MemoryPressureService (validates size limits, allocation, release, safety checks) in tests/PerfProblemSimulator.Tests/Unit/MemoryPressureServiceTests.cs
- [X] T028 [P] [US2] Integration test for POST /api/allocate-memory and POST /api/release-memory endpoints in tests/PerfProblemSimulator.Tests/Integration/MemoryEndpointTests.cs

### Implementation for User Story 2

- [X] T029 [P] [US2] Create MemoryAllocationRequest model with validation attributes in src/PerfProblemSimulator/Models/MemoryAllocationRequest.cs
- [X] T030 [P] [US2] Create AllocatedMemoryBlock domain model in src/PerfProblemSimulator/Models/AllocatedMemoryBlock.cs
- [X] T031 [US2] Create IMemoryPressureService interface in src/PerfProblemSimulator/Services/IMemoryPressureService.cs
- [X] T032 [US2] Implement MemoryPressureService with pinned byte arrays (with educational comments explaining LOH and GC behavior) in src/PerfProblemSimulator/Services/MemoryPressureService.cs
- [X] T033 [US2] Create MemoryController with POST /api/allocate-memory and POST /api/release-memory endpoints in src/PerfProblemSimulator/Controllers/MemoryController.cs
- [X] T034 [US2] Register IMemoryPressureService in Program.cs DI container
- [X] T035 [US2] Add request logging for memory operations (FR-010)

**Checkpoint**: User Story 2 complete - Memory pressure can be triggered and released independently

---

## Phase 5: User Story 3 - Trigger Thread Pool Starvation (Priority: P2)

**Goal**: Enable users to trigger sync-over-async thread pool starvation to observe this subtle anti-pattern

**Independent Test**: Invoke POST /api/trigger-sync-over-async multiple times concurrently and observe response times for other endpoints increase

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T036 [P] [US3] Unit test for ThreadBlockService (validates delay/concurrency limits) in tests/PerfProblemSimulator.Tests/Unit/ThreadBlockServiceTests.cs
- [ ] T037 [P] [US3] Integration test for POST /api/trigger-sync-over-async endpoint in tests/PerfProblemSimulator.Tests/Integration/ThreadBlockEndpointTests.cs

### Implementation for User Story 3

- [ ] T038 [P] [US3] Create ThreadBlockRequest model with validation attributes in src/PerfProblemSimulator/Models/ThreadBlockRequest.cs
- [ ] T039 [US3] Create IThreadBlockService interface in src/PerfProblemSimulator/Services/IThreadBlockService.cs
- [ ] T040 [US3] Implement ThreadBlockService with Task.Delay().Result (with PROMINENT educational comments explaining why this is bad) in src/PerfProblemSimulator/Services/ThreadBlockService.cs
- [ ] T041 [US3] Create ThreadBlockController with POST /api/trigger-sync-over-async endpoint in src/PerfProblemSimulator/Controllers/ThreadBlockController.cs
- [ ] T042 [US3] Register IThreadBlockService in Program.cs DI container
- [ ] T043 [US3] Add request logging for thread blocking operations (FR-010)

**Checkpoint**: User Story 3 complete - Thread pool starvation can be triggered and observed independently

---

## Phase 6: User Story 4 - View Application Health Dashboard (Priority: P3)

**Goal**: Provide real-time dashboard showing CPU, memory, and thread pool metrics via SignalR WebSocket

**Independent Test**: Navigate to / and observe metrics updating in real-time; trigger any simulation and see metrics change within 5 seconds

### Tests for User Story 4 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T044 [P] [US4] Unit test for MetricsCollector (validates metric collection on dedicated thread) in tests/PerfProblemSimulator.Tests/Unit/MetricsCollectorTests.cs
- [ ] T045 [P] [US4] Integration test for GET /api/metrics endpoints in tests/PerfProblemSimulator.Tests/Integration/MetricsEndpointTests.cs
- [ ] T046 [US4] Integration test for SignalR MetricsHub connection in tests/PerfProblemSimulator.Tests/Integration/SignalRIntegrationTests.cs

### Implementation for User Story 4

#### Metrics Models

- [ ] T047 [P] [US4] Create MetricsSnapshot record struct for SignalR updates in src/PerfProblemSimulator/Models/MetricsSnapshot.cs
- [ ] T048 [P] [US4] Create CpuMetrics model in src/PerfProblemSimulator/Models/CpuMetrics.cs
- [ ] T049 [P] [US4] Create MemoryMetrics model in src/PerfProblemSimulator/Models/MemoryMetrics.cs
- [ ] T050 [P] [US4] Create ThreadPoolMetrics model in src/PerfProblemSimulator/Models/ThreadPoolMetrics.cs
- [ ] T051 [P] [US4] Create ApplicationHealthStatus model in src/PerfProblemSimulator/Models/ApplicationHealthStatus.cs
- [ ] T052 [P] [US4] Create ActiveSimulationSummary model in src/PerfProblemSimulator/Models/ActiveSimulationSummary.cs

#### Metrics Collection Service

- [ ] T053 [US4] Create IMetricsCollector interface in src/PerfProblemSimulator/Services/IMetricsCollector.cs
- [ ] T054 [US4] Implement MetricsCollector with dedicated Thread (not ThreadPool) for FR-013 responsiveness in src/PerfProblemSimulator/Services/MetricsCollector.cs

#### SignalR Hub

- [ ] T055 [US4] Create IMetricsClient interface for strongly-typed hub in src/PerfProblemSimulator/Hubs/IMetricsClient.cs
- [ ] T056 [US4] Create MetricsHub SignalR hub in src/PerfProblemSimulator/Hubs/MetricsHub.cs
- [ ] T057 [US4] Implement metrics broadcasting from dedicated thread using IHubContext

#### API Endpoints

- [ ] T058 [US4] Create MetricsController with GET /api/metrics, /api/metrics/cpu, /api/metrics/memory, /api/metrics/threadpool in src/PerfProblemSimulator/Controllers/MetricsController.cs

#### Dashboard SPA

- [ ] T059 [P] [US4] Create dashboard CSS with warning banner styling in src/PerfProblemSimulator/wwwroot/css/dashboard.css
- [ ] T060 [US4] Create index.html with warning banner (FR-014), metrics display areas, and SignalR client script reference in src/PerfProblemSimulator/wwwroot/index.html
- [ ] T061 [US4] Create dashboard.js with SignalR connection, metrics rendering, and auto-reconnect in src/PerfProblemSimulator/wwwroot/js/dashboard.js

#### Service Registration

- [ ] T062 [US4] Register IMetricsCollector as singleton and configure SignalR in Program.cs

**Checkpoint**: User Story 4 complete - Dashboard shows real-time metrics independently

---

## Phase 7: Admin & Control Features

**Purpose**: Cross-cutting control features that affect multiple user stories

### Tests

- [ ] T063 [P] Integration test for POST /api/reset-all endpoint in tests/PerfProblemSimulator.Tests/Integration/AdminEndpointTests.cs

### Implementation

- [ ] T064 Create AdminController with POST /api/reset-all endpoint in src/PerfProblemSimulator/Controllers/AdminController.cs
- [ ] T065 Implement reset logic: cancel active simulations, release memory, optional GC
- [ ] T066 Add request logging for admin operations

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and final validation

- [ ] T067 [P] Add XML documentation comments to all public interfaces and classes explaining educational purpose
- [ ] T068 [P] Create README.md at repository root with project overview, purpose disclaimer, and link to quickstart
- [ ] T069 [P] Create TROUBLESHOOTING.md documenting common issues and solutions
- [ ] T070 Review and enhance inline code comments for anti-pattern explanations (FR-016)
- [ ] T071 Validate all endpoints return proper ErrorResponse on validation failures
- [ ] T072 Run complete test suite and verify all tests pass
- [ ] T073 Execute quickstart.md validation: run locally, trigger each problem type, verify dashboard
- [ ] T074 [P] Create .gitignore with standard .NET exclusions
- [ ] T075 [P] Create docs/azure-monitoring-guide.md describing how to observe each problem type in Azure Portal (FR-017)
- [ ] T076 Integration test verifying multiple problem types can run concurrently (FR-005) in tests/PerfProblemSimulator.Tests/Integration/ConcurrentSimulationsTests.cs

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup)
     │
     ▼
Phase 2 (Foundational) ─────────────────────────────────────────┐
     │                                                           │
     ▼                                                           │
┌────┴────────────────────────────────────────────────────────┐ │
│  Phase 3: US1 (CPU)     Phase 4: US2 (Memory)              │ │
│  Phase 5: US3 (Thread)  Phase 6: US4 (Dashboard)           │◄┘
│  (Can run in parallel after Phase 2 completes)              │
└────┬────────────────────────────────────────────────────────┘
     │
     ▼
Phase 7 (Admin) ─ Depends on US1, US2, US3 services existing
     │
     ▼
Phase 8 (Polish) ─ Final validation
```

### User Story Dependencies

| Story | Depends On | Can Start After |
|-------|------------|-----------------|
| US1 (CPU) | Foundational only | Phase 2 complete |
| US2 (Memory) | Foundational only | Phase 2 complete |
| US3 (Thread) | Foundational only | Phase 2 complete |
| US4 (Dashboard) | Foundational only | Phase 2 complete |

**Note**: All user stories are independently implementable after Phase 2. They share foundational infrastructure but do not depend on each other.

### Within Each User Story

1. Tests MUST be written and FAIL before implementation
2. Models before services
3. Interfaces before implementations
4. Services before controllers
5. Controller before logging integration

---

## Parallel Opportunities

### Phase 1 (Setup)

```
Parallel group: T004, T005, T006, T007 (all config files, no dependencies)
```

### Phase 2 (Foundational)

```
Parallel group: T008, T009, T010, T011 (all model files, no dependencies)
Parallel group: T016, T017 (unit tests, no dependencies)
```

### After Phase 2 Completes

```
US1, US2, US3, US4 can ALL start in parallel (different file sets, no cross-dependencies)
```

### Within User Story 4

```
Parallel group: T047, T048, T049, T050, T051, T052 (all model files)
Parallel group: T044, T045 (tests before implementation)
```

---

## Summary

| Phase | Tasks | Description |
|-------|-------|-------------|
| 1 | T001-T007 | Setup (7 tasks) |
| 2 | T008-T018 | Foundational (11 tasks) |
| 3 | T019-T026 | US1: CPU Stress (8 tasks) 🎯 MVP |
| 4 | T027-T035 | US2: Memory Pressure (9 tasks) |
| 5 | T036-T043 | US3: Thread Blocking (8 tasks) |
| 6 | T044-T062 | US4: Dashboard (19 tasks) |
| 7 | T063-T066 | Admin Features (4 tasks) |
| 8 | T067-T076 | Polish (10 tasks) |

**Total**: 76 tasks

### MVP Scope

To deliver a minimal viable product (CPU stress only):
1. Complete Phase 1 (Setup)
2. Complete Phase 2 (Foundational)
3. Complete Phase 3 (User Story 1 - CPU)

**MVP Task Count**: 26 tasks (T001-T026)

### Suggested MVP+ Scope

For a more complete demo including monitoring:
1. Complete Phases 1-3 (MVP)
2. Add Phase 6 (Dashboard) for observability

**MVP+ Task Count**: 45 tasks

### Full Scope

All phases including documentation and concurrent simulation verification.

**Full Task Count**: 76 tasks
