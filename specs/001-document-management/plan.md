# Implementation Plan: Document Upload and Management

**Branch**: `001-document-management` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-document-management/spec.md`

## Summary

Add secure document upload, browsing, search, management, sharing, task/project/dashboard integration, and admin audit reports to the ContosoDashboard Blazor Server app.

- **Storage**: Files go to the local filesystem outside `wwwroot` through a new `IFileStorageService` abstraction, using GUID keys `{userId}/{projectId|personal}/{guid}{ext}`. Metadata goes to three new EF Core entities (`Document`, `DocumentShare`, `DocumentActivity`) in the existing SQLite database.
- **Business logic**: A new `DocumentService` owns validation, malware scanning (a pluggable `IMalwareScanner` that checks EICAR and magic bytes), upload ordering with compensation, and one shared access predicate used by every read path. Downloads and previews are served by two authorized minimal-API endpoints that return 404 on any denial.

Details are in [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, per `ContosoDashboard.csproj`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, EF Core 10 (SQLite provider), Bootstrap 5.3 / Bootstrap Icons. No new runtime packages.  
**Storage**: SQLite (`ContosoDashboard.db`, created through `EnsureCreated`) for metadata. Local filesystem `ContosoDashboard/AppData/uploads` (configurable `DocumentStorage:RootPath`) for file content.  
**Testing**: New `ContosoDashboard.Tests` xUnit project (SQLite in-memory) for service, authorization, and storage tests, plus manual scenarios in [quickstart.md](quickstart.md)  
**Target Platform**: Local developer machine (Linux, Windows, or macOS), offline  
**Project Type**: Single Blazor Server web application (plus a test project)  
**Performance Goals**: Lists of ≤ 500 docs load in < 2 s; search returns in < 2 s; previews open in < 3 s; a 25 MB upload completes in < 30 s  
**Constraints**: Offline only, no cloud SDKs; 25 MB per file; whitelisted types with server-derived MIME types (browser `Content-Type` ignored); "Personal Files" never linked to a project or task; existing security headers (X-Frame-Options DENY, CSP) unchanged; integer keys; text categories  
**Scale/Scope**: Training dataset (4 seeded users, a few projects, up to about 500 documents per user); 3 new pages, 4 extended pages, 3 new entities, 4 new services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* Evaluated against constitution **v1.0.1**.

| Principle | Gate | Pre-design | Post-design |
|---|---|---|---|
| I. Spec-Driven Development | The spec exists and is clarified; the plan traces to FRs/USs | ✅ spec with 7 clarifications, no open markers | ✅ Contracts and quickstart map to US1–US7 and FR-001–FR-030 |
| II. Security by Default | `[Authorize]` on pages; service-level authorization; IDOR protection; server-side input validation; GUID file names outside `wwwroot`; headers unchanged | ✅ Planned | ✅ Single `VisibleTo` predicate (R6); 404 on denial; role and department read from the DB; minimal-API endpoints with `RequireAuthorization`; served `Content-Type` derived from the whitelisted extension, never the browser (R5); PDF preview in a new tab instead of an iframe keeps `X-Frame-Options: DENY` (R5); path-traversal guard (R1) |
| III. Offline-First + Abstraction | Interfaces for infrastructure, DI-registered local implementations | ✅ Planned | ✅ `IFileStorageService`, `IMalwareScanner` with local implementations; relative storage keys portable to Blob Storage |
| IV. Layered Architecture | Models/Data/Services/Pages; no `DbContext` in pages; int keys; no rewrites | ✅ Planned | ✅ New entities in `Models/`, config in `ApplicationDbContext`, logic in `Services/`, UI in `Pages/`; extends existing pages only |
| V. Simplicity & Verifiable Quality | YAGNI; build clean; acceptance scenarios verified; ordered storage/DB writes | ✅ Planned | ✅ Tags as a normalized column (no tag table), `LIKE` search, no migrations; upload order with compensation (R2); xUnit tests for the security-critical matrix; quickstart scenarios |
| Tech constraints | Stack is ASP.NET Core 10 (.NET 10), EF Core 10 with SQLite; no network packages | ✅ Matches `ContosoDashboard.csproj` (`net10.0`) | ✅ No new runtime packages. Test-only packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) are justified in R11. |
| Workflow & quality gates | Spec, plan, and task gates; implementation gate; review before merge to `main` | ✅ Spec and plan gates met | ✅ Work happens on branch `001-document-management`; build/test/quickstart gates and a constitution review PR are part of the tasks |

**Gate result**: PASS. No violations against constitution v1.0.1.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-management/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── http-endpoints.md
│   └── service-interfaces.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Program.cs                         # + DI registrations, DocumentStorage options, 2 minimal-API endpoints
├── appsettings.json                   # + "DocumentStorage": { "RootPath": "AppData/uploads", "MaxFileSizeBytes": 26214400 }
├── AppData/uploads/                   # runtime only (git-ignored)
├── Data/
│   └── ApplicationDbContext.cs        # + Documents, DocumentShares, DocumentActivities, relationships, indexes
├── Models/
│   ├── Document.cs                    # new
│   ├── DocumentShare.cs               # new
│   ├── DocumentActivity.cs            # new
│   ├── DocumentCategories.cs          # new (fixed category list + DocumentFileTypes extension→MIME map)
│   └── Notification.cs                # + DocumentShared, DocumentAdded
├── Services/
│   ├── IFileStorageService.cs         # new interface
│   ├── LocalFileStorageService.cs     # new
│   ├── IMalwareScanner.cs             # new interface + ScanResult
│   ├── LocalMalwareScanner.cs         # new
│   ├── DocumentService.cs             # new (IDocumentService + DTOs)
│   ├── DocumentReportService.cs       # new (IDocumentReportService)
│   └── DashboardService.cs            # + DocumentCount
├── Pages/
│   ├── Documents.razor                # new: My Documents, search, upload/edit/replace/delete/share
│   ├── SharedDocuments.razor          # new: Shared with Me
│   ├── DocumentReports.razor          # new: admin reports
│   ├── ProjectDetails.razor           # + project documents section
│   ├── Tasks.razor                    # + per-task documents panel/upload
│   ├── Index.razor                    # + Recent Documents widget, document count card
│   └── Login.cshtml.cs                # + Department claim
└── Shared/
    ├── NavMenu.razor                  # + Documents / Reports links
    └── DocumentUploadModal.razor      # new reusable upload component (used by Documents, ProjectDetails, Tasks)

ContosoDashboard.Tests/                # new xUnit project
├── ContosoDashboard.Tests.csproj
├── TestDb.cs                          # in-memory SQLite + fake storage helpers
├── DocumentServiceAuthorizationTests.cs
├── DocumentServiceUploadTests.cs
├── DocumentServiceProjectTests.cs
├── DocumentServiceSearchTests.cs
├── DocumentServiceManageTests.cs
├── DocumentServiceShareTests.cs
├── DocumentServiceTaskDashboardTests.cs
├── DocumentReportServiceTests.cs
├── LocalMalwareScannerTests.cs
└── LocalFileStorageServiceTests.cs

.gitignore                             # + **/AppData/
```

**Structure Decision**: Keep the existing single Blazor Server project and its Models/Data/Services/Pages/Shared layering (constitution principle IV). Add one sibling test project, `ContosoDashboard.Tests`, which references `ContosoDashboard`. The repo has no solution file; tests run with `dotnet test ContosoDashboard.Tests`. There is no task detail page, so task document integration (US6) lives in the existing `Tasks.razor` as an expandable per-task panel.

## Complexity Tracking

No constitution violations require justification. The extra test project and test-only NuGet packages are additive and offline. They are justified in [research.md](research.md) R11 as the verification mechanism for SC-009 and SC-010.
