<!--
  ============================================================================
  SYNC IMPACT REPORT
  ============================================================================
  Version change: N/A (initial) → 1.0.0
  
  Modified Principles: None (initial creation)
  
  Added Sections:
    - Core Principles (5 principles)
    - Code Quality Standards
    - Development Workflow
    - Governance
  
  Removed Sections: None (initial creation)
  
  Templates Status:
    - plan-template.md: ✅ Compatible (Constitution Check section present)
    - spec-template.md: ✅ Compatible (Requirements align with principles)
    - tasks-template.md: ✅ Compatible (Phase structure supports TDD)
  
  Follow-up TODOs: None
  ============================================================================
-->

# NETCoreApp Constitution

## Core Principles

### I. Code Quality First

All code MUST prioritize clarity, maintainability, and correctness over brevity or cleverness.

**Non-negotiable rules:**

- Every public method, class, and interface MUST include XML documentation comments
- Documentation MUST explain the "why" (intent and rationale), not just the "what" (behavior)
- Code MUST be written to assist learning developers in understanding patterns and decisions
- Complex logic MUST include inline comments explaining the approach in broad terms
- Naming MUST be descriptive and self-documenting (no abbreviations except industry-standard ones)

**Rationale:** Code is read far more often than it is written. Clear, well-documented code reduces onboarding time, prevents bugs, and enables confident refactoring.

### II. Test-First Development (NON-NEGOTIABLE)

Tests MUST be written before implementation code, following strict TDD discipline.

**Non-negotiable rules:**

- Test cases MUST be defined and approved before implementation begins
- The Red-Green-Refactor cycle MUST be strictly followed:
  1. Write a failing test (Red)
  2. Write minimal code to pass the test (Green)
  3. Refactor while keeping tests green (Refactor)
- Unit tests MUST cover all public interfaces
- Integration tests MUST verify cross-component interactions

**Rationale:** TDD ensures code meets requirements from the start, produces better-designed interfaces, and creates a safety net for future changes.

### III. Clean Architecture

Code MUST follow separation of concerns and dependency inversion principles.

**Non-negotiable rules:**

- Business logic MUST reside in the domain/core layer with no external dependencies
- Dependencies MUST point inward (infrastructure → application → domain)
- Interfaces MUST be used for external dependencies (database, APIs, file system)
- Each class/module MUST have a single, well-defined responsibility
- Circular dependencies are FORBIDDEN

**Rationale:** Clean architecture enables testability, replaceability of components, and long-term maintainability as requirements evolve.

### IV. Defensive Programming

Code MUST anticipate and handle error conditions gracefully.

**Non-negotiable rules:**

- All public method parameters MUST be validated; use guard clauses at method entry
- Exceptions MUST be caught at appropriate boundaries with meaningful error messages
- Null values MUST be handled explicitly (use nullable reference types, null checks, or Option patterns)
- All external calls (APIs, database, file I/O) MUST include appropriate error handling
- Logging MUST capture sufficient context for debugging production issues

**Rationale:** Defensive code fails predictably and provides actionable information when things go wrong, reducing debugging time and improving user experience.

### V. Simplicity & YAGNI

Code MUST solve the current requirement without over-engineering for hypothetical future needs.

**Non-negotiable rules:**

- Implement only what is explicitly required; no speculative features
- Prefer composition over inheritance
- Avoid premature optimization; measure before optimizing
- Choose the simplest solution that meets requirements
- Complexity MUST be justified in code comments or design documents

**Rationale:** Unnecessary complexity increases maintenance burden, introduces bugs, and makes code harder to understand. Simple code is easier to test, debug, and extend.

## Code Quality Standards

### Documentation Requirements

| Element | Documentation Required |
|---------|----------------------|
| Public classes | XML summary describing purpose and usage |
| Public methods | XML summary, param descriptions, return description, exception documentation |
| Complex algorithms | Inline comments explaining the approach step-by-step |
| Configuration | Comments explaining valid values and effects |
| Magic numbers/strings | Named constants with explanatory comments |

### Code Review Checklist

All code reviews MUST verify:

- [ ] XML documentation is complete and meaningful
- [ ] Tests exist and follow TDD principles
- [ ] Error handling is comprehensive
- [ ] No code smells (long methods, large classes, duplicate code)
- [ ] Naming is clear and consistent
- [ ] SOLID principles are followed

### Testing Standards

- **Unit tests:** Minimum 80% code coverage for business logic
- **Integration tests:** Required for all external integrations
- **Naming convention:** `MethodName_Scenario_ExpectedBehavior`
- **Arrangement:** Follow Arrange-Act-Assert pattern

## Development Workflow

### Feature Implementation Process

1. **Specification:** Define requirements and acceptance criteria in `/specs/`
2. **Planning:** Create implementation plan with Constitution Check gate
3. **Test Writing:** Write failing tests covering all acceptance criteria
4. **Implementation:** Write code to pass tests, following principles above
5. **Refactoring:** Clean up while maintaining passing tests
6. **Review:** Verify compliance with this constitution before merge

### Quality Gates

Before any PR can be merged:

- [ ] All tests pass
- [ ] Code coverage meets thresholds
- [ ] No compiler warnings
- [ ] Documentation is complete
- [ ] Constitution Check in plan.md shows no violations

## Governance

This constitution supersedes all other development practices for this project.

### Amendment Process

1. Proposed changes MUST be documented with rationale
2. Changes MUST be reviewed by project stakeholders
3. Version number MUST be incremented according to semantic versioning:
   - **MAJOR:** Removing principles or backward-incompatible governance changes
   - **MINOR:** Adding new principles or materially expanding guidance
   - **PATCH:** Clarifications, wording improvements, non-semantic refinements
4. All affected templates and documentation MUST be updated to reflect changes

### Compliance

- All pull requests MUST include a Constitution Check section confirming compliance
- Violations MUST be documented and justified if proceeding
- Repeated violations warrant process review and potential team discussion

**Version**: 1.0.0 | **Ratified**: 2026-01-29 | **Last Amended**: 2026-01-29
