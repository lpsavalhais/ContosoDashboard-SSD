# Contract: HTTP Endpoints and UI Routes

All routes require an authenticated cookie session. Authorization is enforced by `IDocumentService` (see [service-interfaces.md](service-interfaces.md)). The existing security headers apply unchanged.

## HTTP endpoints (minimal APIs in `Program.cs`, `.RequireAuthorization()`)

### `GET /documents/{id:int}/download`

| Case | Status | Body / headers |
|---|---|---|
| User may view the document | 200 | File bytes. `Content-Type: {FileType}` (server-derived from the extension, never the browser value). `Content-Disposition: attachment; filename*=UTF-8''{OriginalFileName}`. `Cache-Control: no-store`. `X-Content-Type-Options: nosniff` |
| Document missing **or** user not permitted | 404 | Empty (existence not revealed, FR-016) |
| Not authenticated | 302 → `/login` | Cookie auth challenge |

Side effect: writes a `DocumentActivity` record with `Action = Download`.

### `GET /documents/{id:int}/preview`

Same as download, with these differences:
- Only for `.pdf`, `.png`, `.jpg`, `.jpeg`. Other types return **404**.
- `Content-Disposition: inline; filename*=UTF-8''{OriginalFileName}`.
- Side effect: `DocumentActivity` with `Action = Preview`.
- UI usage: PDFs open in a new tab (`target="_blank"`), and images use `<img src>`. No iframes (`X-Frame-Options: DENY` stays).

## Blazor routes (pages)

| Route | Page | Authorization | Purpose |
|---|---|---|---|
| `/documents` | `Pages/Documents.razor` | `[Authorize]` | My Documents: list, sort, filter, search, upload modal, edit, replace, delete, share (US1, US3, US4, US5) |
| `/documents/shared` | `Pages/SharedDocuments.razor` | `[Authorize]` | Shared with Me (US5) |
| `/documents/reports` | `Pages/DocumentReports.razor` | `[Authorize(Policy = "Administrator")]` | Activity reports (US7) |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (extended) | existing | Project documents section and upload (US2) |
| `/tasks` | `Pages/Tasks.razor` (extended) | existing | Per-task documents panel and upload-to-task (US6) |
| `/` | `Pages/Index.razor` (extended) | existing | Recent Documents widget and document count card (US6) |

`Shared/NavMenu.razor` gains a "Documents" link. The "Reports" link is shown only to Administrators, as UI convenience; the page and service still enforce authorization.

## User-facing messages (stable texts for acceptance tests)

| Condition | Message |
|---|---|
| File > 25 MB | "{file} exceeds the 25 MB limit." |
| Unsupported type | "{file} is not a supported type. Supported: PDF, Word, Excel, PowerPoint, text, JPEG, PNG." |
| Scan failed | "{file} failed the security check and was not uploaded." |
| Missing title / category | "Title is required." / "Category is required." |
| Not permitted to target project | "You can only add documents to projects you belong to." |
| Personal Files with project or task | "Personal Files cannot be linked to a project or task." |
| Upload success | "{n} document(s) uploaded successfully." |
| Not found / no access | "Document not found." |
| Search empty | "No documents match your search." |
