<!--
Sync Impact Report
- Version change: 1.0.0 → 1.0.1 (PATCH)
- Modified principles: none
- Modified sections: Technology and Training Constraints → Stack updated from ASP.NET Core 8.0
  to ASP.NET Core 10 (.NET 10, net10.0) with EF Core 10 and SQLite, matching ContosoDashboard.csproj
- Added sections: none
- Removed sections: none
- Follow-up TODOs: none
-->

# ContosoDashboard Constitution

## Core Principles

### I. Spec-Driven Development

- Every feature MUST start from a specification (`spec.md`) describing user value, scenarios,
  and measurable acceptance criteria before any implementation plan or code is written.
- Implementation MUST follow the Spec Kit flow: specify → clarify (as needed) → plan → tasks →
  implement. Plans MUST pass the Constitution Check before tasks are generated.
- Code changes MUST trace back to a task, and tasks MUST trace back to a requirement in the spec.
- Stakeholder documents (e.g., `StakeholderDocs/`) are inputs to specs, not substitutes for them.

**Rationale**: This repository exists to teach Spec-Driven Development; traceability from
requirement to code is the core learning outcome.

### II. Security by Default (NON-NEGOTIABLE)

- Every protected page MUST carry the `[Authorize]` attribute (or a role/policy variant).
- Every service method returning or mutating user data MUST verify the requesting user's
  authorization (ownership, project membership, or role) — UI checks alone are insufficient.
- Direct object access by ID MUST be guarded against IDOR; unauthorized requests MUST return
  not-found/forbidden without leaking existence of the resource.
- User-supplied input MUST be validated server-side. User-supplied file names MUST NOT be used
  in file system paths; stored files MUST use generated (GUID-based) names, live outside
  `wwwroot`, and be served only through authorized endpoints.
- Existing security headers, cookie settings, and defense-in-depth layers MUST NOT be weakened.

**Rationale**: The application models secure-by-design practices (OWASP Top 10); new features
must preserve user isolation and role-based access control.

### III. Offline-First with Infrastructure Abstraction

- The application MUST run fully offline with no cloud subscriptions or external services.
- Every infrastructure dependency (database, file storage, authentication, messaging) MUST be
  consumed through an interface (e.g., `IFileStorageService`) registered via dependency
  injection, with a local implementation for training.
- Business logic, UI, and database schema MUST NOT depend on a concrete infrastructure
  implementation, so cloud equivalents (Azure SQL, Blob Storage, Entra ID) can be swapped in
  by configuration and DI changes only.

**Rationale**: Training must be available anywhere, while students learn the abstraction
patterns required for production cloud migration.

### IV. Layered Architecture and Consistency

- Code MUST respect the existing layers: `Models` (entities), `Data` (EF Core `DbContext`),
  `Services` (business logic and authorization), `Pages`/`Shared` (Blazor UI).
- Pages MUST NOT access `ApplicationDbContext` directly; they MUST go through services.
- New entities MUST follow existing conventions: integer primary keys, explicit relationships
  and indexes in `ApplicationDbContext`, and text-based values where specs require readability.
- Features MUST integrate into the current architecture; major rewrites or new frameworks
  require a constitution amendment.

**Rationale**: Consistent structure keeps the codebase approachable for learners and
reduces regression risk.

### V. Simplicity and Verifiable Quality

- Prefer the simplest design that satisfies the spec (YAGNI); added complexity MUST be
  justified in the plan's Complexity Tracking section.
- Every change MUST build with `dotnet build` with no errors and no new warnings.
- Each user story MUST define independent acceptance scenarios that are verified (manually or
  by automated tests) before the story is marked complete.
- Data-changing operations that span storage and database MUST be ordered to avoid orphaned or
  inconsistent records (e.g., generate unique path → save file → persist metadata).
- Performance targets stated in a spec MUST be considered in the plan and validated.

**Rationale**: Small, verifiable increments demonstrate good practice within a training
context and keep the starter project stable.

## Technology and Training Constraints

- **Stack**: ASP.NET Core 10 (.NET 10, `net10.0`), Blazor Server, Entity Framework Core 10 with
  SQLite, Bootstrap 5.3 with Bootstrap Icons. Deviations require an amendment.
- **Authentication**: Cookie-based mock authentication with claims and roles (Employee, Team
  Lead, Project Manager, Administrator). New features MUST reuse it rather than introduce a
  separate identity mechanism.
- **Storage**: Local SQLite database and local file system (outside `wwwroot`) only.
- **Dependencies**: New NuGet packages MUST be justified in the plan and MUST NOT require
  network services at runtime.
- **Scope**: The project is for TRAINING PURPOSES ONLY; limitations that differ from
  production expectations MUST be documented rather than hidden.

## Development Workflow and Quality Gates

1. **Specification gate**: `spec.md` has no unresolved `[NEEDS CLARIFICATION]` markers before
   planning.
2. **Plan gate**: `plan.md` includes a Constitution Check that passes (or documents justified
   exceptions) before and after design.
3. **Task gate**: `tasks.md` is organized by user story, with each story independently
   implementable and testable.
4. **Implementation gate**: build succeeds, acceptance scenarios pass, authorization is enforced
   at the service layer, and no secrets or user-controlled paths are introduced.
5. **Review gate**: changes are reviewed against this constitution before merge to `main`.

## Governance

- This constitution supersedes other project practices; conflicts are resolved in its favor.
- Amendments MUST be made via `/speckit.constitution`, include a Sync Impact Report, and be
  committed with a message describing the change and version bump.
- Versioning follows semantic versioning: MAJOR for removing or redefining principles, MINOR
  for adding principles/sections or materially expanding guidance, PATCH for clarifications.
- Every plan and review MUST verify compliance; non-compliance MUST be fixed or explicitly
  justified in Complexity Tracking. `README.md` provides runtime development guidance.

**Version**: 1.0.1 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20
