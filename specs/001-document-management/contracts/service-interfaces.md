# Contract: Service Interfaces

These are signatures and behavioral contracts only. The implementations belong to the tasks and implement phases. All `requestingUserId` methods return `null`, `false`, or an empty result when access is denied, never an exception that reveals existence. This matches the existing `ProjectService` pattern.

## `IFileStorageService` (infrastructure abstraction, constitution principle III)

```csharp
public interface IFileStorageService
{
    // Stores the stream under the given relative key. Returns the key.
    Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType);
    Task<Stream> DownloadAsync(string storageKey);   // throws FileNotFoundException if missing
    Task DeleteAsync(string storageKey);             // no-op if missing
    Task<string> GetUrlAsync(string storageKey, TimeSpan expiration); // reserved for Blob SAS URLs; local impl is not used by the UI (downloads go through /documents/{id}/download)
}
```

- **Local implementation**: `LocalFileStorageService`, rooted at `DocumentStorage:RootPath`. Rejects any key that resolves outside the root.
- **Future implementation**: `AzureBlobStorageService`, swapped in through DI with no changes to callers.

## `IMalwareScanner`

```csharp
public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(Stream content, string fileExtension); // leaves stream Position = 0
}
public record ScanResult(bool IsClean, string? Reason);
```

- **Local implementation**: `LocalMalwareScanner` checks the EICAR signature and the magic bytes for the extension ([research.md](../research.md) R3).

## `IDocumentService`

```csharp
public interface IDocumentService
{
    // Upload (US1, US2, US6)
    Task<UploadResult> UploadAsync(UploadBatchRequest request, int requestingUserId);

    // Query (US1, US2, US3, US5, US6)
    Task<List<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<List<DocumentSummary>> GetProjectDocumentsAsync(int projectId, int requestingUserId);
    Task<List<DocumentSummary>> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<List<DocumentSummary>> GetSharedWithMeAsync(int requestingUserId);
    Task<List<DocumentSummary>> SearchAsync(string term, int requestingUserId);
    Task<List<DocumentSummary>> GetRecentAsync(int requestingUserId, int count = 5);
    Task<int> GetMyDocumentCountAsync(int requestingUserId);
    Task<DocumentSummary?> GetByIdAsync(int documentId, int requestingUserId);

    // Access for HTTP endpoints (logs Download/Preview)
    Task<DocumentContent?> OpenForReadAsync(int documentId, int requestingUserId, bool isPreview);

    // Management (US4)
    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);
    Task<OperationResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId);
    Task<bool> DeleteAsync(int documentId, int requestingUserId);

    // Sharing (US5)
    Task<OperationResult> ShareAsync(int documentId, ShareRequest request, int requestingUserId);
}
```

### DTOs (shape only)

| Type | Fields |
|---|---|
| `FileUpload` | `Stream Content`, `string OriginalFileName`, `string ContentType`, `long Size`, `string Title` |
| `UploadBatchRequest` | `List<FileUpload> Files`, `string Category`, `string? Description`, `string? Tags`, `int? ProjectId`, `int? TaskId` |
| `UploadResult` | `List<int> CreatedDocumentIds`, `List<FileError> Errors` (per-file `FileName`, `Message`) |
| `DocumentQuery` | `string? Category`, `int? ProjectId`, `DateTime? From`, `DateTime? To`, `string SortBy` (Title\|UploadedDate\|Category\|FileSize), `bool Descending` |
| `DocumentSummary` | `DocumentId`, `Title`, `Description`, `Category`, `Tags` (list), `FileType`, `FileSizeBytes`, `UploadedDate`, `UploaderName`, `ProjectId?`, `ProjectName?`, `TaskId?`, `CanEdit`, `CanDelete`, `CanReplace`, `CanShare`, `CanPreview` |
| `DocumentMetadataUpdate` | `Title`, `Description?`, `Category`, `Tags?` |
| `ShareRequest` | `List<int> UserIds`, `List<string> Departments` |
| `DocumentContent` | `Stream Content`, `string ContentType`, `string FileName` |
| `OperationResult` | `bool Success`, `string? ErrorMessage` |

### Behavioral guarantees

1. **Access rule**: Every read path applies the same `VisibleTo(user)` predicate ([research.md](../research.md) R6).
2. **Batch upload**: Each file is validated and stored independently. Valid files succeed even when others fail (edge case).
3. **Consistency**: No document row exists without a stored file, and no stored file exists without a row, after `UploadAsync` returns (FR-009).
4. **Task upload**: `TaskId` forces `ProjectId` to the task's project.
5. **Notifications**: Project upload notifies the PM and members except the uploader (FR-027). Share notifies only new recipients, excluding the sharer (FR-024).
6. **Audit**: Upload, Download, Preview, Replace, Delete, and Share each write exactly one `DocumentActivity` record per document affected (FR-028).

## `IDocumentReportService` (US7)

```csharp
public interface IDocumentReportService
{
    Task<DocumentReport?> GetReportAsync(int requestingUserId, DateTime fromUtc, DateTime toUtc); // null unless Administrator
}
```

`DocumentReport` contains:
- `TopFileTypes` (FileType, UploadCount)
- `TopUploaders` (UserName, UploadCount)
- `AccessByDocument` (Title, Downloads, Previews)
- `AccessByDay` (Date, Downloads, Previews)

## Changes to existing interfaces

- `IDashboardService.GetDashboardSummaryAsync`: `DashboardSummary` gains `int DocumentCount`.
- `NotificationType`: append `DocumentShared` and `DocumentAdded`.
