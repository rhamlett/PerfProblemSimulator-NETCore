# Feature Specification: Performance Problem Simulator

**Feature Branch**: `001-perf-problem-simulator`  
**Created**: 2026-01-29  
**Status**: Draft  
**Input**: User description: "Build an Azure App Service test application that produces performance problems like high CPU, high memory usage, and sync over async thread blocking for demonstration, testing, and learning purposes"

## Overview

This application intentionally creates controllable performance problems commonly encountered in production Azure App Service environments. The purpose is educational: to provide a safe sandbox where developers, support engineers, and learners can:

1. **Observe** how different performance anti-patterns manifest in monitoring tools
2. **Practice** diagnosing issues using Azure diagnostics, Application Insights, and profiling tools
3. **Learn** to recognize symptoms and root causes of common performance problems

> ⚠️ **Important**: This application deliberately implements "bad" code patterns. It is intended **only** for demonstration, testing, and learning in non-production environments.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trigger High CPU Usage (Priority: P1)

As a developer or support engineer, I want to trigger a controllable high-CPU condition so that I can observe how CPU saturation appears in Azure monitoring tools and practice diagnosing the root cause.

**Why this priority**: High CPU is the most common and visible performance problem. It affects all requests and is easily observable in Azure metrics, making it ideal for initial learning.

**Independent Test**: Can be fully tested by invoking a single endpoint and observing CPU metrics spike in Azure Portal or local Task Manager. Delivers immediate, visible feedback for learners.

**Acceptance Scenarios**:

1. **Given** the application is running and idle, **When** I invoke the high-CPU endpoint with a duration parameter, **Then** CPU usage rises to near 100% for the specified duration and returns to baseline afterward.

2. **Given** I have Azure Monitor or Application Insights configured, **When** I trigger high CPU for 60 seconds, **Then** the CPU spike is visible in the metrics dashboard within 2 minutes.

3. **Given** I invoke the high-CPU endpoint multiple times concurrently, **When** monitoring the application, **Then** each invocation contributes to sustained high CPU until all complete.

---

### User Story 2 - Trigger High Memory Usage (Priority: P2)

As a developer or support engineer, I want to trigger controllable memory pressure so that I can observe memory exhaustion patterns, understand garbage collection behavior, and practice diagnosing memory issues.

**Why this priority**: Memory problems are the second most common issue. They manifest differently than CPU issues and teach learners about managed memory, GC pressure, and potential out-of-memory conditions.

**Independent Test**: Can be fully tested by invoking the memory-pressure endpoint and observing memory metrics grow. The application should allow releasing the memory afterward to demonstrate recovery.

**Acceptance Scenarios**:

1. **Given** the application is running with baseline memory usage, **When** I invoke the high-memory endpoint requesting allocation of a specific size, **Then** memory usage increases by approximately that amount.

2. **Given** I have allocated memory via the endpoint, **When** I invoke the release-memory endpoint, **Then** memory usage returns toward baseline (after GC runs).

3. **Given** I repeatedly allocate memory without releasing, **When** the process approaches its memory limit, **Then** the application remains responsive and reports the memory pressure condition rather than crashing unexpectedly.

---

### User Story 3 - Trigger Thread Pool Starvation via Sync-over-Async (Priority: P2)

As a developer or support engineer, I want to trigger thread pool starvation through sync-over-async blocking so that I can observe how this anti-pattern causes request queuing, timeouts, and apparent "hangs" even when CPU and memory appear normal.

**Why this priority**: Sync-over-async is a subtle but devastating problem. It's harder to diagnose than CPU/memory issues because traditional metrics look normal while the application becomes unresponsive. Understanding this pattern is critical for async programming mastery.

**Independent Test**: Can be tested by invoking the sync-over-async endpoint repeatedly and observing increasing response times for unrelated endpoints, demonstrating thread pool exhaustion.

**Acceptance Scenarios**:

1. **Given** the application is running normally, **When** I invoke the sync-over-async endpoint multiple times concurrently, **Then** response times for all endpoints increase as the thread pool becomes exhausted.

2. **Given** I am monitoring thread pool statistics, **When** I trigger sync-over-async blocking, **Then** I can observe thread pool queue depth increasing and available threads decreasing.

3. **Given** the thread pool is starved, **When** the blocking operations complete, **Then** the application recovers and response times return to normal.

---

### User Story 4 - View Application Health Dashboard (Priority: P3)

As a learner, I want a simple web dashboard showing the current state of the application (CPU, memory, thread pool status) so that I can observe the effects of my actions in real-time without requiring Azure Portal access.

**Why this priority**: While Azure monitoring is the primary learning target, a built-in dashboard provides immediate feedback and works for local development scenarios without Azure connectivity.

**Independent Test**: Can be tested by loading the dashboard page and verifying it displays current metrics that update as conditions change.

**Acceptance Scenarios**:

1. **Given** the application is running, **When** I navigate to the dashboard, **Then** I see current CPU percentage, memory usage, and thread pool statistics.

2. **Given** I trigger a high-CPU condition, **When** I view the dashboard, **Then** the CPU metric reflects the elevated usage within 5 seconds.

3. **Given** I am viewing the dashboard, **When** I trigger any performance problem, **Then** the relevant metrics update to reflect the current state.

---

### Edge Cases

- What happens when a user requests more memory than available? The application should cap allocation at a safe maximum and report the limitation rather than crashing.
- What happens when duration parameters are extremely large? The application should enforce maximum duration limits to prevent indefinite resource consumption.
- How does the system handle concurrent triggering of different problem types? All problem simulators should operate independently without interfering with each other.
- What happens if the release-memory endpoint is called when no memory has been allocated? The operation should succeed gracefully with no effect.

## Requirements *(mandatory)*

### Functional Requirements

**Core Problem Simulators**

- **FR-001**: System MUST provide an endpoint to trigger sustained high CPU usage for a configurable duration (default: 30 seconds, maximum: 300 seconds)
- **FR-002**: System MUST provide an endpoint to allocate a configurable amount of memory that persists until explicitly released (default: 100 MB, maximum: 1 GB)
- **FR-003**: System MUST provide an endpoint to release previously allocated memory
- **FR-004**: System MUST provide an endpoint that demonstrates sync-over-async thread blocking with configurable concurrency and delay parameters
- **FR-005**: System MUST allow multiple problem types to be active simultaneously

**Observability & Feedback**

- **FR-006**: System MUST expose current CPU usage percentage via an endpoint
- **FR-007**: System MUST expose current memory usage (working set, GC heap) via an endpoint
- **FR-008**: System MUST expose thread pool statistics (available threads, pending work items, completed work items) via an endpoint
- **FR-009**: System MUST provide a web-based dashboard as a single-page application with real-time WebSocket updates that displays all metrics in a human-readable format
- **FR-010**: System MUST log all problem-trigger operations with timestamps and parameters for learning review

**Safety & Control**

- **FR-011**: System MUST enforce maximum limits on all configurable parameters to prevent accidental resource exhaustion
- **FR-012**: System MUST provide a "reset all" endpoint that releases allocated memory and allows pending operations to complete
- **FR-013**: System MUST remain responsive to the health/status endpoints even under simulated stress conditions by using a separate dedicated thread for metrics collection with async-safe queuing
- **FR-014**: System MUST display a prominent warning banner on the dashboard indicating this is a testing tool not for production use
- **FR-015**: System MUST support an environment variable (e.g., `DISABLE_PROBLEM_ENDPOINTS`) that disables all problem-triggering endpoints when set

**Documentation**

- **FR-016**: System MUST include inline code comments explaining each performance anti-pattern and why it causes problems
- **FR-017**: System MUST include documentation describing how to observe each problem type in Azure monitoring tools

### Key Entities

- **Problem Simulation Request**: Represents a request to trigger a specific performance problem, including type (CPU/Memory/ThreadBlock), parameters (duration, size, concurrency), and timestamp
- **Application Health Status**: Represents the current state of the application including CPU percentage, memory metrics, thread pool statistics, and active problem simulations
- **Allocated Memory Block**: Represents memory intentionally held by the application, including size and allocation timestamp, for the memory pressure simulation

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can trigger high CPU (>90% utilization) within 5 seconds of endpoint invocation and sustain it for the requested duration (±5 seconds accuracy)
- **SC-002**: Users can allocate memory in 10 MB increments up to 1 GB, with actual allocation within 10% of requested amount
- **SC-003**: Thread pool starvation symptoms (response time increase >500% for unrelated requests) are observable within 30 seconds of triggering sync-over-async with 100 concurrent operations
- **SC-004**: Dashboard metrics refresh within 5 seconds of actual state changes
- **SC-005**: Application recovers to baseline performance within 60 seconds after stopping all problem simulations
- **SC-006**: All code implementing anti-patterns includes explanatory comments that a junior developer can understand
- **SC-007**: A learner with basic Azure knowledge can successfully trigger and observe each problem type within 15 minutes using the included documentation

## Assumptions

- The application will be deployed to Azure App Service for production demonstration, but will also run locally for development and learning
- Users have basic familiarity with web APIs and HTTP requests (e.g., can use a browser, curl, or Postman)
- Azure monitoring tools (Azure Monitor, Application Insights) are available but not required for basic functionality; Application Insights integration is optional via configuration and can be enabled through Azure Portal
- The target runtime is .NET (version to be determined in planning phase)
- Memory allocation limits are constrained by the App Service plan tier; documentation will note this dependency

## Clarifications

### Session 2026-01-29

- Q: Should this application have access controls to prevent unauthorized use of problem-triggering endpoints? → A: No authentication; add warning banner and environment variable to disable problem endpoints
- Q: Should Application Insights integration be built-in or optional? → A: Optional via configuration; can enable through Azure Portal later
- Q: What UI approach for the dashboard? → A: Single-page application with real-time WebSocket updates (aligns with typical user configurations)
- Q: What API style for problem-triggering endpoints? → A: Action-oriented RPC style (e.g., `/api/trigger-high-cpu`, `/api/release-memory`)
- Q: How should health/metrics endpoints remain responsive under thread pool starvation? → A: Use separate dedicated thread for metrics collection with async-safe queuing
