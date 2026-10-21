# Phase 0 Research: Document Upload and Management

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-20

All Technical Context items were resolvable from the codebase, the constitution, the stakeholder document, and the clarification session. No open `NEEDS CLARIFICATION` items remain.

---

## R1. File storage abstraction and on-disk layout

- **Decision**: Introduce `IFileStorageService` (`UploadAsync`, `DownloadAsync`, `DeleteAsync`, `GetUrlAsync`) with a `LocalFileStorageService` implementation. Files go under a configurable root (`DocumentStorage:RootPath`, default `AppData/uploads`, resolved relative to the content root, outside `wwwroot`). Relative storage keys follow `{uploaderUserId}/{projectId|"personal"}/{guid}{ext}`. Only the relative key is persisted in `Document.FilePath`.
- **Rationale**: This is required by constitution principle III and by the stakeholder document. Relative keys are portable and map 1:1 to Azure Blob names. GUID names block path traversal and collisions (FR-008). Storing outside `wwwroot` forces all reads through authorized endpoints.
- **Hardening**: `LocalFileStorageService` resolves `Path.GetFullPath(Path.Combine(root, key))` and rejects any result not under `root`. The extension comes from a whitelist and is lowercased; it is never taken raw from user input.
- **Alternatives considered**: Store BLOBs in SQLite. Rejected because it bloats the DB, breaks the cloud-migration story, and makes 25 MB rows slow. Keep original file names on disk. Rejected for security and collision reasons.

## R2. Upload ordering and consistency (FR-009, SC-010)

- **Decision**: In `DocumentService.UploadAsync`, the order is: validate (size, extension, metadata) → authorize (project membership) → scan → generate the GUID key → save the file → insert the DB record → on DB failure, delete the saved file (compensation) → write the activity record → send notifications (best effort, after commit).
- **Replace file**: Save the new file under a new key → update the record → delete the old file (failures are logged, never rolled back into user-visible errors).
- **Delete**: Remove the DB record and its shares in one `SaveChanges` → delete the file (a failure is logged as an orphan file, which is harmless and has no dangling DB pointer).
- **Rationale**: Generating the path before insertion prevents empty or duplicate `FilePath` values, the defect the stakeholders called out. Compensation guarantees no orphaned record and no orphaned file on upload failure.
- **Alternatives considered**: Distributed transaction or two-phase approach. Overkill for local disk and not portable to Blob Storage.

## R3. Malware check in offline training (FR-006, Clarification Q2)

- **Decision**: `IMalwareScanner.ScanAsync(Stream, extension)` returns `ScanResult { IsClean, Reason }`. `LocalMalwareScanner` implements two checks:
  1. **EICAR detection**: reject if the content contains the 68-byte EICAR test signature.
  2. **Content/type match (magic bytes)**:
     - `.pdf` must start with `%PDF-`.
     - `.png` must start with `89 50 4E 47 0D 0A 1A 0A`.
     - `.jpg`/`.jpeg` must start with `FF D8 FF`.
     - `.docx`/`.xlsx`/`.pptx` must start with `PK 03 04`.
     - `.doc`/`.xls`/`.ppt` must start with `D0 CF 11 E0 A1 B1 1A E1`.
     - `.txt` must not contain NUL bytes in the first 8 KB.
- **Rationale**: This works offline with no external engine, gives a deterministic and testable rejection path, and can be swapped for a ClamAV or Defender implementation through DI (constitution principle III).
- **Alternatives considered**: Skip scanning (rejected in Q2). Require ClamAV (rejected because it adds an install dependency and blocks offline training).

## R4. Blazor Server file upload pattern and progress (FR-001, FR-007)

- **Decision**:
  - Use `<InputFile multiple>` with an `@key` that changes after each upload to reset the component.
  - Capture `Name`, `Size`, and `ContentType` before opening the stream.
  - Call `OpenReadStream(maxAllowedSize: 25 MB)`, copy in 81,920-byte chunks into a `MemoryStream`, and update a per-file percentage plus `StateHasChanged()` while copying.
  - Clear the `IBrowserFile` reference afterwards.
  - Pass the `MemoryStream` (Position = 0) to `DocumentService`.
  - Check size client-side before reading to fail fast (FR-003), and enforce it again server-side in the service.
  - Batch metadata (category, project, description, tags) is entered once. Per-file titles are pre-filled with `Path.GetFileNameWithoutExtension(name)` (Clarification Q4).
- **Rationale**: This avoids `IBrowserFile` disposal and reuse errors noted by the stakeholders. Chunked copying gives real progress feedback without JS interop.
- **Alternatives considered**: An MVC upload endpoint with JS `fetch` progress. Rejected because it adds an antiforgery and JS surface. Blazor `InputFile` already streams over SignalR.

## R5. Serving downloads and previews securely (FR-014, FR-015, FR-019)

- **Decision**: Add two minimal API endpoints in `Program.cs`, each with `.RequireAuthorization()`:
  - `GET /documents/{id:int}/download` returns `Content-Disposition: attachment` with the original file name.
  - `GET /documents/{id:int}/preview` returns `inline`, and only for PDF, PNG, and JPEG.
  
  Both resolve the user ID from the `NameIdentifier` claim and call `IDocumentService.OpenForReadAsync(id, userId, isPreview)`, which enforces the access rule and logs the activity. Unauthorized or missing documents return **404** (no existence leak). Both endpoints add `Cache-Control: no-store`.
- **Content-Type**: The served `Content-Type` is always the canonical type for the whitelisted extension, never the browser-supplied value:
  - `.pdf` application/pdf
  - `.doc` application/msword
  - `.docx` application/vnd.openxmlformats-officedocument.wordprocessingml.document
  - `.xls` application/vnd.ms-excel
  - `.xlsx` application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
  - `.ppt` application/vnd.ms-powerpoint
  - `.pptx` application/vnd.openxmlformats-officedocument.presentationml.presentation
  - `.txt` text/plain; charset=utf-8
  - `.jpg`/`.jpeg` image/jpeg
  - `.png` image/png
  
  This prevents a file that passes the magic-byte check from being served inline as `text/html` (stored XSS).
- **PDF preview**: Open in a new browser tab (`target="_blank"`), not an `<iframe>`, so the existing `X-Frame-Options: DENY` and CSP stay unchanged (constitution principle II). Image previews render with `<img src="/documents/{id}/preview">`, which is allowed by `img-src 'self'`.
- **Rationale**: Blazor components cannot stream file responses, so HTTP endpoints are required. Minimal APIs avoid adding MVC controllers to a project that has none.
- **Alternatives considered**: An MVC controller (more ceremony, same outcome). An iframe preview (would require relaxing `X-Frame-Options`, which violates the constitution).

## R6. Authorization model (FR-016–FR-019, FR-024, Clarifications Q1/Q3/Q5)

- **Decision**: Centralize the access rules in `DocumentService` and build them as an EF-translatable `IQueryable<Document>` filter (`VisibleTo(user)`). This filter is used for single fetches, lists, search, and counts. The requesting user's `Role` and `Department` are **loaded from the database by user ID** (authoritative), not trusted from claims.

  | Capability | Who |
  |---|---|
  | View / preview / download | Administrator; uploader; PM or member of the doc's project; direct share recipient; member of a shared department; TeamLead of the uploader's department when Category ≠ "Personal Files" |
  | Edit metadata / delete | Administrator; uploader; PM of the doc's project; TeamLead of the uploader's department when Category ≠ "Personal Files" |
  | Replace file / share | Uploader only (plus Administrator for replace) |
  | Associate with project on upload | PM or member of that project (Administrator: any). Never allowed for category "Personal Files" (also blocks a task link) |
  | Reports | Administrator only (page policy + service check) |

- **Rationale**: A single query predicate prevents drift between lists, search, and direct access (SC-009). Share recipients get read-only access (Q5). Using the database copy of role and department avoids stale-claim problems.
- **Also**: Add the `Department` claim at login (stakeholder note) for UI use only. No authorization decision depends on it.

## R7. Tags, categories, and search (FR-004, FR-011, FR-013, SC-003/004)

- **Decision**:
  - `Category` is stored as text (max 50) and validated against a static list `DocumentCategories.All`.
  - Tags are stored in one normalized text column (`Tags`, max 500): lowercase, trimmed, de-duplicated, comma-delimited with leading and trailing commas (e.g., `,budget,q3,`), so `LIKE '%,budget,%'` matches exact tags.
  - Search applies `VisibleTo(user)` and then an OR across `Title`, `Description`, `Tags`, `UploadedBy.DisplayName`, and `Project.Name` using `EF.Functions.Like` with a `%term%` pattern. Wildcards in user input are escaped.
  - Lists use `AsNoTracking()`, projection to a summary DTO, and indexes on `UploadedByUserId`, `ProjectId`, `TaskId`, `UploadedDate`, and `Category`.
- **Rationale**: At the training scale (≤ 500 documents per list), SQLite `LIKE` completes well under 2 s. A separate Tag table adds joins and UI complexity for no measurable benefit (constitution principle V).
- **Alternatives considered**: A Tag/DocumentTag many-to-many design (rejected as YAGNI). SQLite FTS5 (rejected: it requires raw SQL and complicates the Azure SQL migration).

## R8. Sharing and notifications (FR-023, FR-024, FR-027)

- **Decision**: A `DocumentShare` row targets either `SharedWithUserId` or `SharedWithDepartment` (exactly one, enforced by validation and a check constraint). Filtered unique indexes prevent duplicate shares. Department membership is evaluated at access time, so later joiners gain access. On share, notifications go to recipients who did not already have access through that share and are not the sharer. Append `DocumentShared` and `DocumentAdded` to `NotificationType` (appending keeps existing int values stable). Project upload notifications go to the PM and members, excluding the uploader.
- **Rationale**: This meets the "no duplicate entries or notifications" edge case and reuses `INotificationService`.

## R9. Audit and reports (FR-028–FR-030)

- **Decision**:
  - The `DocumentActivity` table stores `DocumentId` (no FK, so records survive deletion), snapshots of `DocumentTitle` and `FileType`, `UserId`, `Action` (text: Upload, Download, Preview, Delete, Share, Replace), and `Timestamp`.
  - Reports in `DocumentReportService` are LINQ group-bys: upload counts by `FileType`, top uploaders by Upload count, and access patterns (download/preview counts per document and per day for the last 30 days).
  - The reports page is `/documents/reports` with `[Authorize(Policy = "Administrator")]`.
- **Rationale**: Simple, offline, and keeps the audit trail after permanent deletion.

## R10. Schema creation with `EnsureCreated`

- **Decision**: Keep the existing `EnsureCreated()` approach (no migrations in this repo). New tables require deleting the local SQLite file once, which is documented in [quickstart.md](quickstart.md). Add `AppData/` to `.gitignore`.
- **Rationale**: This is consistent with the current architecture (constitution principle IV). Introducing migrations would be a broader change outside this feature.

## R11. Automated verification

- **Decision**: Add a small `ContosoDashboard.Tests` xUnit project. It covers the `DocumentService` authorization matrix and upload/delete consistency using SQLite in-memory, plus `LocalMalwareScanner` and `LocalFileStorageService` path-safety tests. UI flows are verified manually per [quickstart.md](quickstart.md).
- **Rationale**: SC-009 (zero unauthorized access) and SC-010 (no orphans) are security-critical and combinatorial, so automated tests verify them cheaply and repeatably (constitution principles II and V). New packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) are test-only and need no network at runtime.
- **Alternatives considered**: Manual testing only. Rejected because the role × relationship × category matrix is too large to check by hand reliably.

## R12. Target framework (constitution) — resolved

- **Finding**: Constitution v1.0.0 listed "ASP.NET Core 8.0", but `ContosoDashboard.csproj` already targeted `net10.0` with EF Core 10.0.13 (a pre-existing upgrade, not introduced by this feature).
- **Decision**: Constitution amended to **v1.0.1** (PATCH), which lists ASP.NET Core 10 (.NET 10, `net10.0`) with EF Core 10 and SQLite. The feature builds on that stack with no deviation.
