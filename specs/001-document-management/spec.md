# Feature Specification: Document Upload and Management

**Feature Branch**: `001-document-management`  
**Created**: 2026-10-20  
**Status**: Draft  
**Input**: User description: "--file StakeholderDocs/document-upload-and-management-feature.md" (Document Upload and Management Feature – Requirements)

## Clarifications

### Session 2026-10-20

- Q: What should "team" mean when deciding which documents a Team Lead can manage and who receives a document shared "with a team"? → A: Team = department. A Team Lead manages documents from users in their department, and sharing goes to all users in a department.
- Q: How should uploaded files be checked for malware in the offline training environment? → A: Use a local, replaceable check: confirm the file content matches its declared type and reject a known test-virus file (EICAR, a harmless standard test file that antivirus tools flag). A real antivirus engine can be plugged in later.
- Q: Should a Team Lead be able to see and manage documents a member of their department uploaded as "Personal Files" with no project? → A: No. Team Leads see and manage department documents in every category except Personal Files, which only the owner, people it's explicitly shared with, and Administrators can access.
- Q: When a user uploads several files at once, how should the title and other details be entered? → A: Category, project, description, and tags are entered once for the whole batch. Each file's title defaults to its file name (without extension) and can be edited before upload.
- Q: What can a user do with a document someone has shared with them (directly or through their department)? → A: Read-only: recipients can view, preview, and download. They cannot edit, replace, delete, or re-share.
- Q: Can a "Personal Files" document be linked to a project or task? → A: No. The combination is rejected on upload and on category edit.
- Q: Who can edit, replace, and delete beyond the uploader? → A: Project Managers of the document's project and Administrators can edit and delete; Administrators can also replace the file. Team Leads per FR-018.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload and view my documents (Priority: P1)

An employee uploads one or more work documents from their computer, giving each a title, a category, and optionally a description, project, and tags. The documents then appear in a "My Documents" list where the employee can sort and filter them and download any of them.

**Why this priority**: This is the core value of the feature. Without uploading and retrieving documents, nothing else works. It alone replaces scattered local drives and email attachments with one central, secure location.

**Independent Test**: Log in as any user, upload a supported file with the required metadata, confirm it appears in My Documents with correct details, then download it and confirm the content matches the original.

**Acceptance Scenarios**:

1. **Given** a logged-in employee on the upload screen, **When** they select a 2 MB PDF, enter a title, choose the "Reports" category, and submit, **Then** they see upload progress followed by a success message, and the document appears in My Documents with title, category, upload date, file size, and project (if any).
2. **Given** a logged-in employee, **When** they select three supported files in one upload action, choose a category once for the batch, and accept or edit the per-file titles (pre-filled from file names without extension), **Then** all three documents are stored with the shared category and their individual titles.
3. **Given** a user selects a file larger than 25 MB, **When** they try to upload it, **Then** the upload is rejected with a clear message stating the 25 MB limit and nothing is stored.
4. **Given** a user selects an unsupported file type (e.g., an executable), **When** they try to upload it, **Then** the upload is rejected with a message listing the supported types.
5. **Given** a user selects a `.txt` file containing the EICAR test string, or a `.pdf` file whose content is not a PDF, **When** they upload it, **Then** the upload is rejected with a security-check failure message and nothing is stored.
6. **Given** a user omits the title or category, **When** they submit, **Then** the form indicates the missing required fields and does not upload.
7. **Given** a user has 20 documents in My Documents, **When** they sort by file size or filter by category, project, or date range, **Then** the list updates to show only matching documents in the chosen order.
8. **Given** a user owns a document, **When** they choose download, **Then** they receive the original file with its original name and type.

---

### User Story 2 - Project documents for project members (Priority: P2)

Project members see every document associated with a project they belong to, from the project's detail view. Project Managers can upload documents to their projects. Project members are notified when a new document is added to one of their projects.

**Why this priority**: This gives visibility into which documents belong to which project, a main business problem the stakeholders identified. It builds on Story 1.

**Independent Test**: As a Project Manager, upload a document linked to a project. Log in as a member of that project and confirm the document is visible and downloadable from the project view and that a notification was received. Log in as a non-member and confirm the document is not accessible.

**Acceptance Scenarios**:

1. **Given** a document associated with Project A, **When** any member of Project A opens the Project A detail view, **Then** the document is listed and can be downloaded.
2. **Given** a user who is not a member of Project A, **When** they try to view or download a Project A document (including by direct link), **Then** access is denied and the document's existence is not revealed.
3. **Given** a Project Manager of Project A, **When** they upload a document to Project A, **Then** all other Project A members receive an in-app notification about the new document.
4. **Given** an employee assigned to Project A, **When** they upload a document and associate it with Project A, **Then** the upload succeeds. **Given** they are not assigned to Project B, **When** they try to associate a document with Project B, **Then** the request is rejected.

---

### User Story 3 - Search for documents (Priority: P2)

A user searches across all documents they are allowed to access by title, description, tags, uploader name, or associated project, and gets results quickly.

**Why this priority**: It directly addresses the "difficulty locating documents" problem and the "locate a document in under 30 seconds" success metric.

**Independent Test**: With documents from several users and projects in place, search for a tag or title fragment and confirm that only matching documents the user is permitted to see are returned, within 2 seconds.

**Acceptance Scenarios**:

1. **Given** documents tagged "budget" exist in the user's projects and in other projects, **When** the user searches "budget", **Then** only documents the user has permission to access are returned.
2. **Given** a user searches by an uploader's name or a project name, **When** results are shown, **Then** they include matching documents the user can access.
3. **Given** no documents match, **When** the user searches, **Then** a clear "no results" message is shown.

---

### User Story 4 - Manage my documents (edit, replace, delete) (Priority: P3)

A document owner edits a document's title, description, category, or tags, replaces the file with an updated version, or permanently deletes the document after confirming.

**Why this priority**: This keeps information accurate over time, but it is not required to get first value from the feature.

**Independent Test**: Upload a document, edit its metadata, replace its file, and confirm that downloads return the new file. Then delete it and confirm it no longer appears anywhere and cannot be downloaded.

**Acceptance Scenarios**:

1. **Given** a user owns a document, **When** they change its title and tags and save, **Then** the updated metadata appears in all lists and search results.
2. **Given** a user owns a document, **When** they replace the file with a new supported file, **Then** subsequent downloads return the new file and the file size and type are updated.
3. **Given** a user owns a document, **When** they choose delete and confirm, **Then** the document and its file are permanently removed. **When** they cancel instead, **Then** nothing changes.
4. **Given** a Project Manager of Project A, **When** they delete a document in Project A uploaded by someone else, **Then** the deletion succeeds after confirmation.
5. **Given** a user who has no manage permission for a document under FR-020 and FR-022, **When** they try to edit or delete it, **Then** the action is denied.

---

### User Story 5 - Share documents with colleagues (Priority: P3)

A document owner shares a document with specific users or with a department. Recipients get an in-app notification and find the document in a "Shared with Me" section.

**Why this priority**: Controlled sharing replaces risky email and shared-drive sharing, but it depends on upload and access control being in place first.

**Independent Test**: As user A, share a document with user B. Log in as B, confirm a notification was received, that the document appears in Shared with Me, and that it can be downloaded. Log in as C (not shared with) and confirm there is no access.

**Acceptance Scenarios**:

1. **Given** a user owns a document, **When** they share it with a specific colleague, **Then** the colleague receives an in-app notification and the document appears in their Shared with Me section.
2. **Given** a user owns a document, **When** they share it with a department, **Then** every current member of that department gains access and is notified.
3. **Given** a document has not been shared with a user and the user has no other permission to it, **When** that user tries to access it, **Then** access is denied.
4. **Given** a document was shared with a user, **When** that user tries to edit, replace, delete, or re-share it, **Then** the action is denied. Viewing, previewing, and downloading remain allowed.

---

### User Story 6 - Documents in tasks and on the dashboard (Priority: P3)

From a task's detail view, users see documents attached to the task and can upload a document directly to it. The document is automatically associated with the task's project. The dashboard home page shows a "Recent Documents" widget with the user's last 5 uploads and a document count in the summary cards.

**Why this priority**: These integrations bring documents into the places users already work every day, which raises adoption. They add convenience, not core capability.

**Independent Test**: Open a task, upload a document from it, and confirm the document is attached to the task and associated with the task's project. Go to the dashboard and confirm the document appears in Recent Documents and that the document count has increased.

**Acceptance Scenarios**:

1. **Given** a user is viewing a task in Project A, **When** they upload a document from the task view, **Then** the document is attached to the task and associated with Project A.
2. **Given** a user has uploaded 7 documents, **When** they open the dashboard, **Then** the Recent Documents widget shows their 5 most recent uploads, newest first, and the summary card shows the correct document count.

---

### User Story 7 - Activity audit and reports for administrators (Priority: P4)

All document activities (upload, download, delete, share) are recorded. Administrators view reports on the most uploaded document types, the most active uploaders, and document access patterns. Administrators can access all documents for audit and compliance.

**Why this priority**: This supports compliance and the "zero security incidents" goal, but end users can get value without it.

**Independent Test**: Perform uploads, downloads, shares, and deletes as several users. Log in as Administrator and confirm that each action appears in the activity record and that the three reports reflect the activity. Confirm non-administrators cannot open the reports.

**Acceptance Scenarios**:

1. **Given** users have uploaded, downloaded, shared, and deleted documents, **When** an Administrator views the activity reports, **Then** they see the most uploaded document types, the most active uploaders, and access patterns that reflect those actions.
2. **Given** a non-administrator, **When** they try to open the reports, **Then** access is denied.

---

### Edge Cases

- A file exactly at 25 MB is accepted. A file of 25 MB plus 1 byte is rejected.
- A file whose extension is supported but whose content does not match the declared type, or which fails the malware check, is rejected and not stored. The user sees a clear message.
- In a multi-file upload, if some files are invalid, the valid files are still uploaded and each invalid file is reported individually.
- If storing the file fails partway through, no document record is left behind. If saving the document record fails, the stored file is cleaned up. The user sees an error and may retry.
- Two documents uploaded with the same original file name (by the same or different users) are stored independently. Neither overwrites the other.
- Original file names containing path characters (e.g., `../`) or unusual characters cannot affect where the file is stored.
- When a user is removed from a project, they immediately lose access to that project's documents, unless the documents were individually shared with them.
- A Team Lead who opens or searches for a department member's "Personal Files" document that was not shared with them is denied access, and the document does not appear in their lists or search results. If the owner later changes the document's category away from "Personal Files", the Team Lead gains access.
- When a project member who uploaded documents leaves the project, the documents remain with the project.
- Changing the category of a project- or task-linked document to "Personal Files" is rejected with "Personal Files cannot be linked to a project or task."
- A task without a project offers no "Upload to task" action; an attempt to upload to it is rejected with "This task has no project; upload from My Documents instead."
- Deleting a document that has been shared removes it from all recipients' Shared with Me sections.
- Sharing a document with someone who already has access does not create duplicate entries or duplicate notifications.
- A user with no documents sees a helpful empty state in My Documents, Shared with Me, and the Recent Documents widget.
- Preview is offered only for PDF and image files. Other types offer download only.

## Requirements *(mandatory)*

### Functional Requirements

**Upload and validation**

- **FR-001**: Users MUST be able to select and upload one or more files in a single action. In a multi-file upload, category, associated project, description, and tags MUST be entered once and applied to every file in the batch.
- **FR-002**: The system MUST accept only these file types: PDF; Word, Excel, and PowerPoint documents; plain text; JPEG; and PNG. It MUST reject all others with a message listing the supported types.
- **FR-003**: The system MUST reject any file larger than 25 MB with a clear message stating the limit.
- **FR-004**: The system MUST require a title and a category for each document. Each file's title MUST default to its original file name without extension and MUST be editable before upload; an empty title MUST block upload of that file. Category MUST be one of: Project Documents, Team Resources, Personal Files, Reports, Presentations, Other. Description, associated project, and free-form tags MUST be optional. The "Personal Files" category MUST NOT be combined with an associated project or task; such uploads MUST be rejected with "Personal Files cannot be linked to a project or task."
- **FR-005**: The system MUST automatically record the upload date and time, the uploader, the file size, and the file type (including long Office document type identifiers).
- **FR-006**: The system MUST check each uploaded file for malware before making it available. Files that fail MUST be rejected and not retained. In the offline training environment, the check MUST at minimum (a) verify that the file content matches its declared type and (b) detect and reject the EICAR standard test file. The check MUST be replaceable by a full antivirus engine without changing user-facing behavior.
- **FR-007**: The system MUST show upload progress and a success or error message for each file.
- **FR-008**: The system MUST store files using system-generated unique identifiers, never user-supplied file names. Files MUST be stored outside any publicly reachable location so they can be retrieved only through permission-checked access.
- **FR-009**: The system MUST NOT leave a document record without a stored file, or a stored file without a document record, when an upload fails at any step.

**Browsing, search, and access**

- **FR-010**: Users MUST be able to view a "My Documents" list showing title, category, upload date, file size, and associated project.
- **FR-011**: Users MUST be able to sort My Documents by title, upload date, category, and file size, and filter by category, associated project, and date range.
- **FR-012**: When viewing a project, members of that project MUST see and be able to download all documents associated with it.
- **FR-013**: Users MUST be able to search documents by title, description, tags, uploader name, and associated project. Results MUST include only documents the user is permitted to access.
- **FR-014**: Users MUST be able to download any document they are permitted to access. The download MUST use the document's original file name.
- **FR-015**: Users MUST be able to preview PDF and image documents in the browser without downloading.

**Permissions**

- **FR-016**: Access to a document MUST be granted only to: its uploader; members of its associated project; users it has been shared with, directly or via their department; Team Leads of the uploader's department (except for documents in the "Personal Files" category); Project Managers of its associated project; and Administrators. All others MUST be denied without the document's existence being revealed.
- **FR-017**: Employees MUST be able to associate a document only with projects they are members of. Project Managers MUST be able to upload documents to projects they manage.
- **FR-018**: Team Leads MUST be able to view and manage (edit metadata, delete) documents uploaded by users in their own department, excluding documents in the "Personal Files" category. Personal Files are accessible only to their owner, users or departments they are explicitly shared with, and Administrators.
- **FR-019**: Permission checks MUST be enforced on every document operation (list, search, view, preview, download, edit, replace, delete, share), including requests made by direct link.

**Management and sharing**

- **FR-020**: Document uploaders, Project Managers of the document's project, Team Leads per FR-018, and Administrators MUST be able to edit the title, description, category, and tags.
- **FR-021**: Document uploaders and Administrators MUST be able to replace a document's file with an updated version. The replacement is subject to the same validation as an upload. No prior version is retained.
- **FR-022**: Document uploaders MUST be able to delete their documents. Project Managers MUST be able to delete any document in their projects. Administrators MUST be able to delete any document. Deletion MUST require explicit confirmation and MUST permanently remove the document and its file.
- **FR-023**: Document uploaders MUST be able to share a document with specific users or with a department. A department share grants access to all users currently in that department.
- **FR-024**: Recipients of a shared document MUST receive an in-app notification and see the document in a "Shared with Me" section. Shared access is read-only: recipients MAY view, preview, and download, and MUST NOT edit, replace, delete, or re-share the document (unless they hold another permission under FR-016–FR-018). Notifications are not created for users whose in-app notifications are disabled; their access is unaffected.

**Integrations**

- **FR-025**: Users MUST be able to see documents attached to a task and upload a document from the task's detail view. Such documents MUST be automatically associated with the task's project. Uploading to a task that has no project MUST be rejected with "This task has no project; upload from My Documents instead."
- **FR-026**: The dashboard home page MUST show a "Recent Documents" widget listing the user's 5 most recently uploaded documents. The summary cards MUST include the user's document count.
- **FR-027**: Members of a project MUST receive an in-app notification when a new document is added to that project. The uploader is excluded, as are users whose in-app notifications are disabled.

**Audit and reporting**

- **FR-028**: The system MUST record every upload, download, deletion, and share action, including who performed it, on which document, and when.
- **FR-029**: Administrators MUST be able to view reports of the most uploaded document types, the most active uploaders, and document access patterns. Non-administrators MUST NOT have access to these reports.
- **FR-030**: Administrators MUST be able to access all documents for audit and compliance.

### Key Entities *(include if feature involves data)*

- **Document**: An uploaded work file and its metadata: title, optional description, category (stored as readable text), optional tags, original file name, stored file location, file type, file size, upload date and time, uploader, optional associated project, and optional associated task. Uses whole-number identifiers, consistent with existing users and projects.
- **Document Share**: A grant of access to a document. It links the document to either a recipient user or a recipient department, and records who shared it and when.
- **Document Activity**: An audit entry recording an action (upload, download, delete, share) on a document: the acting user, the document, the action type, and the timestamp.
- **Related existing entities**: User (uploader, recipient, role, department), Project (association and membership), Task (attachment), and Notification (share and new-document alerts).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can upload a document in no more than 3 clicks from the documents area, excluding metadata entry.
- **SC-002**: Uploads of files up to 25 MB complete within 30 seconds on a typical network.
- **SC-003**: Document lists with up to 500 documents display within 2 seconds.
- **SC-004**: Searches return results within 2 seconds.
- **SC-005**: Previews of PDF and image documents appear within 3 seconds.
- **SC-006**: Within 3 months of launch, 70% of active dashboard users have uploaded at least one document.
- **SC-007**: Within 3 months of launch, the average time to locate a document is under 30 seconds.
- **SC-008**: Within 3 months of launch, 90% of uploaded documents are in a category other than "Other".
- **SC-009**: Zero incidents of a user accessing a document they are not permitted to access. This is verified by testing every access path with unauthorized users.
- **SC-010**: 100% of failed uploads leave no partial document or orphaned file behind.

## Assumptions

- The feature must work fully offline, using local storage on the machine running the application, for training purposes. The storage location must be swappable for cloud storage later without changing user-facing behavior or stored document data.
- The existing mock login and roles (Employee, Team Lead, Project Manager, Administrator) are used for all permission decisions. The login provides each user's identity, name, email, role, and department.
- Malware checking in the offline training environment uses the local, replaceable check defined in FR-006. A full antivirus engine can be substituted in production.
- "Typical network" means a local or corporate LAN connection. Most documents are under 10 MB.
- Replacing a file overwrites the previous version. Version history is out of scope.
- Deletion is permanent. Soft delete or trash recovery is out of scope.
- Audit records are kept for the lifetime of the training database. No retention policy is required for this release.

### Out of Scope

- Real-time collaborative editing
- Version history and rollback
- Approval workflows and document routing
- Integration with external systems (e.g., SharePoint, OneDrive)
- Mobile app support (web only)
- Document templates or generation
- Storage quotas
- Soft delete / trash with recovery

### Dependencies

- Existing authentication, role, and department information for users
- Existing project membership data, task data, and the in-app notification capability
- Existing dashboard home page and summary cards
