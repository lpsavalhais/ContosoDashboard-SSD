# Quickstart: Validating Document Upload and Management

**Feature**: [spec.md](spec.md) | **Contracts**: [contracts/](contracts/) | **Data model**: [data-model.md](data-model.md)

## Prerequisites

- .NET SDK matching `ContosoDashboard.csproj` (`net10.0`)
- Test files on hand:
  - `sample.pdf` (~2 MB)
  - `big.pdf` (> 25 MB)
  - `exact25.pdf` (exactly 26,214,400 bytes, a valid PDF header)
  - `notes.txt`
  - `photo.png`
  - `tool.exe`
  - `fake.pdf` (a text file renamed to `.pdf`)
  - `eicar.txt` containing the standard EICAR test string

## Setup (one time per schema change)

The app uses `EnsureCreated()` and has no migrations, so delete the local database to pick up the new tables:

```bash
cd ContosoDashboard
rm -f ContosoDashboard.db ContosoDashboard.db-shm ContosoDashboard.db-wal
rm -rf AppData/uploads
dotnet build      # must succeed with no new warnings (constitution V)
dotnet run
```

Open the URL printed by `dotnet run` and log in from the user dropdown.

## Automated checks

```bash
dotnet test ContosoDashboard.Tests
```

Expected: all tests pass, covering:
- The authorization matrix (R6)
- Upload and delete consistency with no orphans (SC-010)
- Malware scanner cases (EICAR and type mismatch)
- Storage path traversal rejection

## Manual validation scenarios

| # | Story | Steps | Expected |
|---|---|---|---|
| 1 | US1 | Ni Kang → Documents → Upload `sample.pdf`, `notes.txt`, `photo.png`; category "Reports"; keep the default titles | Progress shown; "3 document(s) uploaded successfully."; titles = file names without extension |
| 2 | US1 | Upload `big.pdf`, then `exact25.pdf` | `big.pdf` rejected with the 25 MB message; `exact25.pdf` accepted |
| 3 | US1 | Upload `tool.exe`, `fake.pdf`, `eicar.txt` in one batch with `notes.txt` | Three per-file errors (unsupported / security check ×2); `notes.txt` uploaded |
| 4 | US1 | Sort by size and filter by category/date; download `sample.pdf` | List updates; downloaded file matches the original byte for byte and keeps its original name |
| 5 | US2 | Camille Nicole → Project "ContosoDashboard Development" → upload a doc | Floris and Ni receive a "DocumentAdded" notification; Camille does not |
| 6 | US2 | Admin creates a project without Ni; Camille uploads to it; Ni opens `/documents/{id}/download` for that doc | 404 |
| 7 | US3 | Ni searches a tag, then an uploader name, then a nonsense term | Only accessible matches, < 2 s; "No documents match your search." for nonsense |
| 8 | US4 | Ni edits the title and tags, replaces the file, then deletes it (cancel first, then confirm) | Changes reflected; download returns the new file; cancel keeps it; confirm removes it everywhere and the URL returns 404 |
| 9 | US4/Q3 | Ni uploads a "Personal Files" doc (no project); Floris (TeamLead, same department) checks lists and search and opens its URL | Not visible; URL returns 404. After Ni changes the category to "Reports", Floris can see and edit it |
| 10 | US5/Q5 | Ni shares a doc with Camille, and with the department "Engineering" | Camille is notified and sees it in Shared with Me; she can download, but edit, delete, and share are unavailable and rejected by the service |
| 11 | US6 | Ni → Tasks → task "Create UI mockups" → upload a doc | Doc attached to the task and linked to its project; dashboard Recent Documents shows ≤ 5 newest; count card increments |
| 12 | US7 | Admin → `/documents/reports`; Ni → `/documents/reports` | Admin sees top types, top uploaders, and access patterns that reflect the steps above; Ni is denied |
| 13 | Security | View page source and response headers on download and preview | `X-Frame-Options: DENY`, CSP unchanged, `Cache-Control: no-store`; no file is reachable under `/wwwroot` |
| 14 | Storage | Inspect `ContosoDashboard/AppData/uploads` | Files are named `{userId}/{projectId\|personal}/{guid}.{ext}`; no original file names on disk |

## Performance spot checks

- Seed or upload about 500 documents for one user. `/documents` loads in under 2 s, and search returns in under 2 s (SC-003, SC-004).
- A 25 MB upload completes in under 30 s on localhost (SC-002). A PDF or image preview opens in under 3 s (SC-005).
