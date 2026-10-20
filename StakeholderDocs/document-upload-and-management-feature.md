# Document Upload and Management Feature - Requirements

## Overview

Contoso Corporation needs to add document upload and management capabilities to the ContosoDashboard application. This feature will enable employees to upload work-related documents, organize them by category and project, and share them with team members.

## Business Need

Currently, Contoso employees store work documents in various locations (local drives, email attachments, shared drives), leading to:

- Difficulty locating important documents when needed
- Security risks from uncontrolled document sharing
- Lack of visibility into which documents are associated with specific projects or tasks

The document upload and management feature addresses these issues by providing a centralized, secure location for work-related documents within the dashboard application that employees already use daily.

## Target Users

All Contoso employees who use the ContosoDashboard application will have access to document management features, with permissions based on their existing roles:

- **Employees**: Upload personal documents and documents for projects they're assigned to
- **Team Leads**: Upload documents and view/manage documents uploaded by their team members
- **Project Managers**: Upload documents and manage all documents associated with their projects
- **Administrators**: Full access to all documents for audit and compliance purposes

## Core Requirements

### 1. Document Upload

**File Selection and Upload**

- Users must be able to select one or more files from their computer to upload
- Supported file types: PDF, Microsoft Office documents (Word, Excel, PowerPoint), text files, and images (JPEG, PNG)
- Maximum file size: 25 MB per file
- Users should see a progress indicator during upload
- System should display success or error messages after upload completes

**Document Metadata**

- When uploading, users must provide:
  - Document title (required)
  - Description (optional)
  - Category selection from predefined list (required): Project Documents, Team Resources, Personal Files, Reports, Presentations, Other
  - Associated project (optional - if the document relates to a specific project)
  - Tags for easier searching (optional - users can add custom tags)
- System should automatically capture:
  - Upload date and time
  - Uploaded by (user name)
  - File size
  - File type (MIME type, e.g., "application/pdf" - field must accommodate 255 characters for Office documents)

**Validation and Security**

- System must scan uploaded files for viruses and malware before storage
- System must reject files that exceed size limits with clear error messages
- System must reject unsupported file types
- Uploaded files must be stored securely with appropriate access controls

**Implementation Notes for Local File Storage**

**Offline Storage Pattern:**
- Store files in a dedicated directory outside `wwwroot` for security (e.g., `AppData/uploads`)
- Generate unique file paths BEFORE database insertion to prevent duplicate key violations
- Recommended pattern: `{userId}/{projectId or "personal"}/{uniqueId}.{extension}` where uniqueId is a GUID
- **Upload sequence: Generate unique path → Save file to disk → Save metadata to database**
- **This prevents orphaned database records if file save fails**
- **This prevents duplicate key errors from empty or non-unique file paths**

**Security Considerations:**
- Files stored outside `wwwroot` require controller endpoints to serve them (enables authorization checks)
- Validate file extensions against whitelist before saving
- Use GUID-based filenames to prevent path traversal attacks
- Never use user-supplied filenames directly in file paths
- Implement authorization checks in download endpoint to prevent unauthorized access

**Azure Migration Design:**
- Create `IFileStorageService` interface with methods: `UploadAsync()`, `DeleteAsync()`, `DownloadAsync()`, `GetUrlAsync()`
- Local implementation (`LocalFileStorageService`) uses `System.IO.File` operations
- Future `AzureBlobStorageService` implementation will use Azure.Storage.Blobs SDK
- Same path pattern works for Azure blob names: `{userId}/{projectId}/{guid}.{ext}`
- Swap implementations via dependency injection configuration
- No changes to business logic, UI, or database schema required for migration

### 2. Document Organization and Browsing

**My Documents View**

- Users must be able to view a list of all documents they have uploaded
- The view should display: document title, category, upload date, file size, associated project
- Users should be able to sort documents by: title, upload date, category, file size
- Users should be able to filter documents by: category, associated project, date range

**Project Documents View**

- When viewing a specific project, users should see all documents associated with that project
- All project team members should be able to view and download project documents
- Project Managers should be able to upload documents to their projects

**Search**

- Users should be able to search for documents by: title, description, tags, uploader name, associated project
- Search should return results within 2 seconds
- Users should only see documents they have permission to access in search results

### 3. Document Access and Management

**Download and Preview**

- Users must be able to download any document they have access to
- For common file types (PDF, images), users should be able to preview documents in the browser without downloading

**Edit Metadata**

- Users who uploaded a document should be able to edit the document metadata (title, description, category, tags)
- Users should be able to replace a document file with an updated version

**Delete Documents**

- Users should be able to delete documents they uploaded
- Project Managers can delete any document in their projects
- Deleted documents should be permanently removed after user confirmation

**Share Documents**

- Document owners should be able to share documents with specific users or teams
- Users who receive shared documents should be notified via in-app notification
- Shared documents should appear in recipients' "Shared with Me" section

### 4. Integration with Existing Features

**Task Integration**

- When viewing a task, users should be able to see and attach related documents
- Users should be able to upload a document directly from a task detail page
- Documents attached to tasks should automatically be associated with the task's project

**Dashboard Integration**

- Add a "Recent Documents" widget to the dashboard home page showing the last 5 documents uploaded by the user
- Add document count to the dashboard summary cards

**Notifications**

- Users should receive notifications when someone shares a document with them
- Users should receive notifications when a new document is added to one of their projects

### 5. Performance Requirements

- Document upload should complete within 30 seconds for files up to 25 MB (on typical network)
- Document list pages should load within 2 seconds for up to 500 documents
- Document search should return results within 2 seconds
- Document preview should load within 3 seconds

### 6. Reporting and Audit

**Activity Tracking**

- System should log all document-related activities: uploads, downloads, deletions, share actions
- Administrators should be able to generate reports showing:
  - Most uploaded document types
  - Most active uploaders
  - Document access patterns

## User Experience Goals

- **Simplicity**: Uploading a document should require no more than 3 clicks
- **Speed**: Common operations (upload, download, search) should feel instant
- **Clarity**: Users should always know what happens to uploaded files
- **Confidence**: Users should trust that their documents are secure and won't be lost

## Success Metrics

The feature will be considered successful if, within 3 months of launch:

- 70% of active dashboard users have uploaded at least one document
- Average time to locate a document is reduced to under 30 seconds
- 90% of uploaded documents are properly categorized
- Zero security incidents related to document access

## Technical Constraints

- Must work **offline without cloud services** for training purposes
- Must use **local filesystem storage** for uploaded documents
- Must implement **interface abstractions** (`IFileStorageService`) for future cloud migration
- Must work within current application architecture (no major rewrites)
- Must comply with existing mock authentication system
- Development timeline: Feature should be production-ready within 8-10 weeks
- **Database: DocumentId must be integer (not GUID) for consistency with existing User/Project keys**
- **Database: Category must store text values (not integer enum) for simplicity**

## Implementation Approach

The document management feature is built using a **layered architecture** that separates concerns and enables future cloud migration:

**Data Layer:**
- Document entity stores metadata (title, category, filename, file path, upload date, uploader)
- DocumentId uses integer keys (consistent with existing User and Project tables)
- Category stores text values ("Project Documents", "Personal Files", etc.) for simplicity
- FileType field accommodates long MIME types (255 characters for Office documents)
- FilePath accommodates GUID-based filenames for security (prevents path traversal attacks)
- DocumentShare entity tracks sharing relationships between users

**Storage Layer:**
- Files stored outside web-accessible directories (security requirement)
- IFileStorageService interface abstracts storage implementation
- LocalFileStorageService for training (uses local filesystem)
- Future: Swap to AzureBlobStorageService for production (no code changes needed)
- File organization: `{userId}/{projectId or "personal"}/{guid}.{extension}`

**Business Logic Layer:**
- DocumentService orchestrates upload workflow:
  1. Validate file (size limit, extension whitelist)
  2. Authorize user (project membership if uploading to project)
  3. Generate unique GUID-based filename
  4. Save file to disk
  5. Create database record with file path
  6. Send notifications to project members
- Authorization checks prevent unauthorized document access (IDOR protection)
- Service layer enforces all security rules before data access

**Presentation Layer:**
- Blazor Server page for document upload and viewing
- File upload uses MemoryStream pattern (prevents disposal issues in Blazor)
- Responsive table displays user's documents with metadata
- Upload modal validates input before submission

This architecture ensures security, maintainability, and cloud-readiness while keeping the training implementation simple and offline-capable.

### Cloud Migration Readiness

While this feature must work offline for training, it should be designed for easy migration to Azure services:

**Offline Implementation Requirements:**
- Store files in local directory structure (e.g., `AppData/uploads/{userId}/{projectId}/{guid}.ext`)
- Implement `LocalFileStorageService : IFileStorageService` using `System.IO` operations
- File paths stored in database should be relative and portable
- No Azure SDK dependencies in training implementation

**Azure Migration Design Pattern:**

```csharp
// Interface abstraction (implement in training version)
public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteAsync(string filePath);
    Task<Stream> DownloadAsync(string filePath);
    Task<string> GetUrlAsync(string filePath, TimeSpan expiration);
}

// Training: LocalFileStorageService implementation
// Production: AzureBlobStorageService implementation
// Switch via appsettings.json and dependency injection
```

**Migration Benefits:**
- Swap service implementation without changing controllers, pages, or business logic
- Database schema remains unchanged (FilePath column works for both local paths and blob names)
- Configuration-driven deployment (dev = local, production = Azure)
- Students learn industry-standard abstraction patterns

### Blazor-Specific Implementation Requirements

**File Upload Component State Management**

- Use `@key` attribute on `InputFile` component to force re-render after successful upload
- Extract file metadata (name, size, contentType) into local variables BEFORE opening stream
- Copy `IBrowserFile` stream to `MemoryStream` immediately to prevent disposal issues
- Clear `IBrowserFile` reference (set to null) after copying stream to prevent reuse errors
- Example pattern:
  ```csharp
  var fileName = SelectedFile.Name;
  var fileSize = SelectedFile.Size;
  var contentType = SelectedFile.ContentType;
  
  using var memoryStream = new MemoryStream();
  using (var fileStream = SelectedFile.OpenReadStream(maxFileSize))
  {
      await fileStream.CopyToAsync(memoryStream);
  }
  memoryStream.Position = 0;
  
  SelectedFile = null; // Clear reference to prevent reuse
  StateHasChanged();
  ```

**Authentication Claims**

- Ensure Login flow includes ALL required claims: NameIdentifier, Name, Email, Role, Department
- Department claim is required for team-based authorization in document sharing
- Missing claims will cause authorization failures in DocumentService methods

### Database Setup Requirements

**Clean State for Testing:**

- Before testing document upload for the first time, ensure clean database state
- If previous upload attempts failed, drop and recreate database to remove orphaned records:
  ```bash
  # Stop the app first, then delete the SQLite database file (run from the ContosoDashboard project folder)
  rm -f ContosoDashboard.db ContosoDashboard.db-shm ContosoDashboard.db-wal
  # Database will be recreated automatically on next run
  ```
- Orphaned records with empty FilePath values will cause duplicate key violations
- For SQLite: `dotnet ef database drop --force` also works if EF tools are installed

## Assumptions

- Training environment has local disk storage available
- Most documents will be under 10 MB in size
- Users are familiar with basic file management concepts
- Local filesystem storage is acceptable for training purposes
- Cloud migration to Azure Blob Storage is planned for production deployment
- Users may work offline (no internet connection required for core functionality)

## Out of Scope

The following features are NOT included in this initial release:

- Real-time collaborative editing of documents
- Version history and rollback capabilities
- Advanced document workflows (approval processes, document routing)
- Integration with external systems (SharePoint, OneDrive)
- Mobile app support (initial release is web-only)
- Document templates or document generation features
- Storage quotas and quota management
- Soft delete/trash functionality with recovery

These may be considered for future enhancements based on user feedback and business needs.

## Next Steps

Once approved, these requirements will be used to create detailed specifications using the Spec-Driven Development methodology with GitHub Spec Kit.
