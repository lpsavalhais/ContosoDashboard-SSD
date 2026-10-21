using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<UploadResult> UploadAsync(UploadBatchRequest request, int requestingUserId);
    Task<List<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<List<DocumentSummary>> GetProjectDocumentsAsync(int projectId, int requestingUserId);
    Task<List<DocumentSummary>> GetTaskDocumentsAsync(int taskId, int requestingUserId);
    Task<List<DocumentSummary>> GetSharedWithMeAsync(int requestingUserId);
    Task<List<DocumentSummary>> SearchAsync(string term, int requestingUserId);
    Task<List<DocumentSummary>> GetRecentAsync(int requestingUserId, int count = 5);
    Task<int> GetMyDocumentCountAsync(int requestingUserId);
    Task<DocumentSummary?> GetByIdAsync(int documentId, int requestingUserId);
    Task<DocumentContent?> OpenForReadAsync(int documentId, int requestingUserId, bool isPreview);
    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);
    Task<OperationResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId);
    Task<bool> DeleteAsync(int documentId, int requestingUserId);
    Task<OperationResult> ShareAsync(int documentId, ShareRequest request, int requestingUserId);
}

public sealed record FileUpload(Stream Content, string OriginalFileName, string ContentType, long Size, string Title);
public sealed record UploadBatchRequest(List<FileUpload> Files, string Category, string? Description, string? Tags, int? ProjectId, int? TaskId);
public sealed record FileError(string FileName, string Message);
public sealed record UploadResult(List<int> CreatedDocumentIds, List<FileError> Errors);
public sealed record DocumentMetadataUpdate(string Title, string? Description, string Category, string? Tags);
public sealed record ShareRequest(List<int> UserIds, List<string> Departments);
public sealed record DocumentContent(Stream Content, string ContentType, string FileName);
public sealed record OperationResult(bool Success, string? ErrorMessage = null);

public sealed class DocumentQuery
{
    public string? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string SortBy { get; set; } = "UploadedDate";
    public bool Descending { get; set; } = true;
}

public sealed class DocumentSummary
{
    public int DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string FileType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime UploadedDate { get; init; }
    public string UploaderName { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? TaskId { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanReplace { get; init; }
    public bool CanShare { get; init; }
    public bool CanPreview { get; init; }
}

public sealed class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IMalwareScanner _malwareScanner;
    private readonly INotificationService _notificationService;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IMalwareScanner malwareScanner,
        INotificationService notificationService,
        IOptions<DocumentStorageOptions> storageOptions,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _malwareScanner = malwareScanner;
        _notificationService = notificationService;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(UploadBatchRequest request, int requestingUserId)
    {
        var createdIds = new List<int>();
        var errors = new List<FileError>();
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return new UploadResult(createdIds, request.Files.Select(file => new FileError(file.OriginalFileName, "User not found.")).ToList());
        }

        var effectiveProjectId = request.ProjectId;
        if (request.TaskId.HasValue)
        {
            var task = await _context.Tasks.AsNoTracking()
                .FirstOrDefaultAsync(item => item.TaskId == request.TaskId.Value);
            string? taskError = null;
            if (task == null)
            {
                taskError = "Task not found.";
            }
            else if (!task.ProjectId.HasValue)
            {
                taskError = "This task has no project; upload from My Documents instead.";
            }
            else if (user.Role != UserRole.Administrator
                && !await _context.Projects.AnyAsync(project =>
                    project.ProjectId == task.ProjectId.Value
                    && (project.ProjectManagerId == requestingUserId
                        || project.ProjectMembers.Any(member => member.UserId == requestingUserId))))
            {
                taskError = "Task not found.";
            }
            else
            {
                effectiveProjectId = task.ProjectId;
            }

            if (taskError != null)
            {
                return new UploadResult(createdIds, request.Files.Select(file => new FileError(file.OriginalFileName, taskError)).ToList());
            }
        }

        foreach (var file in request.Files)
        {
            var extension = Path.GetExtension(file.OriginalFileName).ToLowerInvariant();
            var validationError = ValidateUpload(file, request, extension);
            if (validationError != null)
            {
                errors.Add(new FileError(file.OriginalFileName, validationError));
                continue;
            }

            if (effectiveProjectId is int projectId
                && user.Role != UserRole.Administrator
                && !await _context.Projects.AnyAsync(project =>
                    project.ProjectId == projectId
                    && (project.ProjectManagerId == requestingUserId
                        || project.ProjectMembers.Any(member => member.UserId == requestingUserId))))
            {
                errors.Add(new FileError(file.OriginalFileName, "You can only add documents to projects you belong to."));
                continue;
            }

            var scan = await _malwareScanner.ScanAsync(file.Content, extension);
            if (!scan.IsClean)
            {
                errors.Add(new FileError(file.OriginalFileName, $"{file.OriginalFileName} failed the security check and was not uploaded."));
                continue;
            }

            var storageKey = $"{requestingUserId}/{effectiveProjectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}{extension}";
            var storageAttempted = false;
            try
            {
                storageAttempted = true;
                await _fileStorage.UploadAsync(file.Content, storageKey, DocumentFileTypes.Map[extension]);

                var document = new Document
                {
                    Title = file.Title.Trim(),
                    Description = request.Description?.Trim(),
                    Category = request.Category,
                    Tags = NormalizeTags(request.Tags),
                    OriginalFileName = Path.GetFileName(file.OriginalFileName),
                    FilePath = storageKey,
                    FileType = DocumentFileTypes.Map[extension],
                    FileExtension = extension,
                    FileSizeBytes = file.Content.Length,
                    UploadedByUserId = requestingUserId,
                    UploadedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    ProjectId = effectiveProjectId,
                    TaskId = request.TaskId
                };
                _context.Documents.Add(document);
                await _context.SaveChangesAsync();
                createdIds.Add(document.DocumentId);

                try
                {
                    await LogActivityAsync(document, requestingUserId, DocumentActions.Upload);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to record document upload activity for {DocumentId}.", document.DocumentId);
                }

                if (document.ProjectId.HasValue)
                {
                    await NotifyProjectUploadAsync(document, requestingUserId);
                }
            }
            catch (Exception exception)
            {
                if (storageAttempted)
                {
                    try
                    {
                        await _fileStorage.DeleteAsync(storageKey);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogError(cleanupException, "Failed to clean up stored document {StorageKey}.", storageKey);
                    }
                }

                _logger.LogError(exception, "Document upload failed for {FileName}.", file.OriginalFileName);
                errors.Add(new FileError(file.OriginalFileName, "The file could not be stored. Please try again."));
            }
        }

        return new UploadResult(createdIds, errors);
    }
    public async Task<List<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return [];
        }

        var documents = _context.Documents
            .AsNoTracking()
            .Where(document => document.UploadedByUserId == requestingUserId);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            documents = documents.Where(document => document.Category == query.Category);
        }

        if (query.ProjectId.HasValue)
        {
            documents = documents.Where(document => document.ProjectId == query.ProjectId.Value);
        }

        if (query.From.HasValue)
        {
            documents = documents.Where(document => document.UploadedDate >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            documents = documents.Where(document => document.UploadedDate < query.To.Value.Date.AddDays(1));
        }

        documents = (query.SortBy, query.Descending) switch
        {
            ("Title", false) => documents.OrderBy(document => document.Title),
            ("Title", true) => documents.OrderByDescending(document => document.Title),
            ("Category", false) => documents.OrderBy(document => document.Category),
            ("Category", true) => documents.OrderByDescending(document => document.Category),
            ("FileSize", false) => documents.OrderBy(document => document.FileSizeBytes),
            ("FileSize", true) => documents.OrderByDescending(document => document.FileSizeBytes),
            ("UploadedDate", false) => documents.OrderBy(document => document.UploadedDate),
            _ => documents.OrderByDescending(document => document.UploadedDate)
        };

        var results = await documents
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .ToListAsync();
        return results.Select(document => ToSummary(document, user)).ToList();
    }
    public async Task<List<DocumentSummary>> GetProjectDocumentsAsync(int projectId, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return [];
        }

        var isAuthorized = user.Role == UserRole.Administrator
            || await _context.Projects.AnyAsync(project =>
                project.ProjectId == projectId
                && (project.ProjectManagerId == requestingUserId
                    || project.ProjectMembers.Any(member => member.UserId == requestingUserId)));
        if (!isAuthorized)
        {
            return [];
        }

        var results = await VisibleTo(user)
            .AsNoTracking()
            .Where(document => document.ProjectId == projectId)
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .OrderByDescending(document => document.UploadedDate)
            .ToListAsync();
        return results.Select(document => ToSummary(document, user)).ToList();
    }
    public async Task<List<DocumentSummary>> GetTaskDocumentsAsync(int taskId, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return [];
        }

        var task = await _context.Tasks.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TaskId == taskId);
        if (task == null)
        {
            return [];
        }

        bool isAuthorized;
        if (user.Role == UserRole.Administrator)
        {
            isAuthorized = true;
        }
        else if (task.ProjectId.HasValue)
        {
            isAuthorized = await _context.Projects.AnyAsync(project =>
                project.ProjectId == task.ProjectId.Value
                && (project.ProjectManagerId == requestingUserId
                    || project.ProjectMembers.Any(member => member.UserId == requestingUserId)));
        }
        else
        {
            isAuthorized = task.AssignedUserId == requestingUserId || task.CreatedByUserId == requestingUserId;
        }

        if (!isAuthorized)
        {
            return [];
        }

        var results = await VisibleTo(user)
            .AsNoTracking()
            .Where(document => document.TaskId == taskId)
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .OrderByDescending(document => document.UploadedDate)
            .ToListAsync();
        return results.Select(document => ToSummary(document, user)).ToList();
    }
    public async Task<List<DocumentSummary>> GetSharedWithMeAsync(int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return [];
        }

        var results = await _context.Documents
            .AsNoTracking()
            .Where(document => document.UploadedByUserId != requestingUserId
                && document.Shares.Any(share =>
                    share.SharedWithUserId == requestingUserId
                    || (user.Department != null
                        && share.SharedWithDepartment != null
                        && share.SharedWithDepartment.ToLower() == user.Department.ToLower())))
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .OrderByDescending(document => document.UploadedDate)
            .ToListAsync();
        return results.Select(document => ToSummary(document, user)).ToList();
    }
    public async Task<List<DocumentSummary>> SearchAsync(string term, int requestingUserId)
    {
        term = term.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return [];
        }

        var pattern = $"%{EscapeLike(term)}%";
        var results = await VisibleTo(user)
            .AsNoTracking()
            .Where(document =>
                EF.Functions.Like(document.Title, pattern, "\\")
                || (document.Description != null && EF.Functions.Like(document.Description, pattern, "\\"))
                || (document.Tags != null && EF.Functions.Like(document.Tags, pattern, "\\"))
                || EF.Functions.Like(document.UploadedBy.DisplayName, pattern, "\\")
                || (document.Project != null && EF.Functions.Like(document.Project.Name, pattern, "\\")))
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .OrderByDescending(document => document.UploadedDate)
            .Take(500)
            .ToListAsync();

        return results.Select(document => ToSummary(document, user)).ToList();
    }
    public async Task<List<DocumentSummary>> GetRecentAsync(int requestingUserId, int count = 5)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null || count <= 0)
        {
            return [];
        }

        var results = await _context.Documents
            .AsNoTracking()
            .Where(document => document.UploadedByUserId == requestingUserId)
            .Include(document => document.UploadedBy)
            .Include(document => document.Project)
            .OrderByDescending(document => document.UploadedDate)
            .Take(Math.Min(count, 100))
            .ToListAsync();
        return results.Select(document => ToSummary(document, user)).ToList();
    }

    public Task<int> GetMyDocumentCountAsync(int requestingUserId) =>
        _context.Documents.CountAsync(document => document.UploadedByUserId == requestingUserId);
    public async Task<DocumentSummary?> GetByIdAsync(int documentId, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return null;
        }

        var document = await VisibleTo(user)
            .AsNoTracking()
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);

        return document == null ? null : ToSummary(document, user);
    }
    public async Task<DocumentContent?> OpenForReadAsync(int documentId, int requestingUserId, bool isPreview)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return null;
        }

        var document = await VisibleTo(user)
            .AsNoTracking()
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null || (isPreview && document.FileExtension is not (".pdf" or ".png" or ".jpg" or ".jpeg")))
        {
            return null;
        }

        Stream content;
        try
        {
            content = await _fileStorage.DownloadAsync(document.FilePath);
        }
        catch (FileNotFoundException exception)
        {
            _logger.LogWarning(exception, "Stored document {DocumentId} has no file.", documentId);
            return null;
        }

        try
        {
            await LogActivityAsync(document, requestingUserId, isPreview ? DocumentActions.Preview : DocumentActions.Download);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to record document access for {DocumentId}.", documentId);
        }

        return new DocumentContent(content, document.FileType, document.OriginalFileName);
    }
    public async Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return false;
        }

        var document = await VisibleTo(user)
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null || !CanManage(document, user)
            || string.IsNullOrWhiteSpace(update.Title) || update.Title.Trim().Length > 255
            || (update.Description?.Length ?? 0) > 2000
            || !DocumentCategories.IsValid(update.Category)
            || (NormalizeTags(update.Tags)?.Length ?? 0) > 500
            || (update.Category == DocumentCategories.PersonalFiles && (document.ProjectId.HasValue || document.TaskId.HasValue)))
        {
            return false;
        }

        document.Title = update.Title.Trim();
        document.Description = update.Description?.Trim();
        document.Category = update.Category;
        document.Tags = NormalizeTags(update.Tags);
        document.UpdatedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<OperationResult> ReplaceFileAsync(int documentId, FileUpload file, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return new OperationResult(false, "Document not found.");
        }

        var document = await VisibleTo(user)
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null || !CanReplace(document, user))
        {
            return new OperationResult(false, "Document not found.");
        }

        var extension = Path.GetExtension(file.OriginalFileName).ToLowerInvariant();
        var request = new UploadBatchRequest([file], document.Category, document.Description, document.Tags, document.ProjectId, document.TaskId);
        var validationError = ValidateUpload(file, request, extension);
        if (validationError != null)
        {
            return new OperationResult(false, validationError);
        }

        var scan = await _malwareScanner.ScanAsync(file.Content, extension);
        if (!scan.IsClean)
        {
            return new OperationResult(false, $"{file.OriginalFileName} failed the security check and was not uploaded.");
        }

        var oldPath = document.FilePath;
        var newPath = $"{document.UploadedByUserId}/{document.ProjectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}{extension}";
        try
        {
            file.Content.Position = 0;
            await _fileStorage.UploadAsync(file.Content, newPath, DocumentFileTypes.Map[extension]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to store replacement for document {DocumentId}.", documentId);
            try
            {
                await _fileStorage.DeleteAsync(newPath);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException, "Failed to clean up replacement {StorageKey}.", newPath);
            }
            return new OperationResult(false, "The replacement file could not be stored.");
        }

        var oldFileType = document.FileType;
        var oldExtension = document.FileExtension;
        var oldSize = document.FileSizeBytes;
        var oldName = document.OriginalFileName;
        var oldUpdatedDate = document.UpdatedDate;
        document.FilePath = newPath;
        document.FileType = DocumentFileTypes.Map[extension];
        document.FileExtension = extension;
        document.FileSizeBytes = file.Size;
        document.OriginalFileName = Path.GetFileName(file.OriginalFileName);
        document.UpdatedDate = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            document.FilePath = oldPath;
            document.FileType = oldFileType;
            document.FileExtension = oldExtension;
            document.FileSizeBytes = oldSize;
            document.OriginalFileName = oldName;
            document.UpdatedDate = oldUpdatedDate;
            _logger.LogError(exception, "Failed to update replacement metadata for document {DocumentId}.", documentId);
            try
            {
                await _fileStorage.DeleteAsync(newPath);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException, "Failed to clean up replacement {StorageKey}.", newPath);
            }
            return new OperationResult(false, "The replacement file could not be saved.");
        }

        try
        {
            await _fileStorage.DeleteAsync(oldPath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to remove replaced document file {StorageKey}.", oldPath);
        }

        try
        {
            await LogActivityAsync(document, requestingUserId, DocumentActions.Replace);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to record document replacement activity for {DocumentId}.", documentId);
        }
        return new OperationResult(true);
    }

    public async Task<bool> DeleteAsync(int documentId, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return false;
        }

        var document = await VisibleTo(user)
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null || !CanManage(document, user))
        {
            return false;
        }

        var filePath = document.FilePath;
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId,
            DocumentTitle = document.Title,
            FileType = document.FileType,
            UserId = requestingUserId,
            Action = DocumentActions.Delete,
            Timestamp = DateTime.UtcNow
        });
        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();

        try
        {
            await _fileStorage.DeleteAsync(filePath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to remove deleted document file {StorageKey}.", filePath);
        }
        return true;
    }
    public async Task<OperationResult> ShareAsync(int documentId, ShareRequest request, int requestingUserId)
    {
        var user = await LoadUserAsync(requestingUserId);
        if (user == null)
        {
            return new OperationResult(false, "Document not found.");
        }

        var document = await VisibleTo(user)
            .Include(item => item.UploadedBy)
            .Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null || !CanShare(document, user))
        {
            return new OperationResult(false, "Document not found.");
        }

        var targetUserIds = request.UserIds.Where(id => id > 0).Distinct().ToList();
        var targetUsers = await _context.Users
            .Where(target => targetUserIds.Contains(target.UserId))
            .ToListAsync();
        if (targetUsers.Count != targetUserIds.Count)
        {
            return new OperationResult(false, "One or more selected users could not be found.");
        }

        var requestedDepartments = request.Departments
            .Select(department => department.Trim())
            .ToList();
        if (requestedDepartments.Any(department => department.Length == 0 || department.Length > 100))
        {
            return new OperationResult(false, "A department name is invalid.");
        }

        var departmentKeys = requestedDepartments
            .Select(department => department.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var departmentUsers = await _context.Users
            .Where(target => target.Department != null && departmentKeys.Contains(target.Department.ToLower()))
            .ToListAsync();
        var canonicalDepartments = departmentKeys.ToDictionary(
            key => key,
            key => departmentUsers.FirstOrDefault(target => target.Department!.ToLower() == key)?.Department
                ?? requestedDepartments.First(department => department.Equals(key, StringComparison.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

        var existingShares = await _context.DocumentShares
            .Where(share => share.DocumentId == documentId)
            .ToListAsync();
        var newUserIds = targetUsers
            .Where(target => target.UserId != requestingUserId
                && existingShares.All(share => share.SharedWithUserId != target.UserId))
            .Select(target => target.UserId)
            .ToList();
        var newDepartments = canonicalDepartments
            .Where(pair => existingShares.All(share =>
                share.SharedWithDepartment == null
                || !share.SharedWithDepartment.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Value)
            .ToList();

        var notificationCandidateIds = new HashSet<int>();
        foreach (var targetUser in targetUsers.Where(target => newUserIds.Contains(target.UserId)))
        {
            if (!await VisibleTo(targetUser).AnyAsync(item => item.DocumentId == documentId))
            {
                notificationCandidateIds.Add(targetUser.UserId);
            }
        }

        foreach (var department in newDepartments)
        {
            var matchingUsers = departmentUsers
                .Where(target => target.Department != null && target.Department.Equals(department, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var targetUser in matchingUsers)
            {
                if (targetUser.UserId != requestingUserId
                    && !newUserIds.Contains(targetUser.UserId)
                    && !await VisibleTo(targetUser).AnyAsync(item => item.DocumentId == documentId))
                {
                    notificationCandidateIds.Add(targetUser.UserId);
                }
            }
        }

        foreach (var userId in newUserIds)
        {
            _context.DocumentShares.Add(new DocumentShare
            {
                DocumentId = documentId,
                SharedWithUserId = userId,
                SharedByUserId = requestingUserId,
                SharedDate = DateTime.UtcNow
            });
        }

        foreach (var department in newDepartments)
        {
            _context.DocumentShares.Add(new DocumentShare
            {
                DocumentId = documentId,
                SharedWithDepartment = department,
                SharedByUserId = requestingUserId,
                SharedDate = DateTime.UtcNow
            });
        }

        if (newUserIds.Count > 0 || newDepartments.Count > 0)
        {
            await _context.SaveChangesAsync();
            try
            {
                await LogActivityAsync(document, requestingUserId, DocumentActions.Share,
                    string.Join(",", newUserIds.Select(id => $"user:{id}").Concat(newDepartments.Select(name => $"department:{name}"))));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to record document share activity for {DocumentId}.", documentId);
            }

            var recipients = await _context.Users
                .Where(target => notificationCandidateIds.Contains(target.UserId) && target.InAppNotificationsEnabled)
                .ToListAsync();
            foreach (var recipient in recipients)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(new Notification
                    {
                        UserId = recipient.UserId,
                        Type = NotificationType.DocumentShared,
                        Priority = NotificationPriority.Informational,
                        Title = "Document shared with you",
                        Message = $"{document.Title} was shared by {user.DisplayName}."
                    });
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to notify user {UserId} about shared document {DocumentId}.", recipient.UserId, documentId);
                }
            }
        }

        return new OperationResult(true);
    }

    private Task<User?> LoadUserAsync(int userId) =>
        _context.Users.AsNoTracking().FirstOrDefaultAsync(user => user.UserId == userId);

    private string? ValidateUpload(FileUpload file, UploadBatchRequest request, string extension)
    {
        if (!DocumentFileTypes.IsAllowed(extension))
        {
            return $"{file.OriginalFileName} is not a supported type. Supported: PDF, Word, Excel, PowerPoint, text, JPEG, PNG.";
        }

        if (file.Size <= 0 || file.Size > _storageOptions.MaxFileSizeBytes
            || !file.Content.CanSeek || file.Content.Length != file.Size
            || file.Content.Length > _storageOptions.MaxFileSizeBytes)
        {
            return file.Size > _storageOptions.MaxFileSizeBytes || (file.Content.CanSeek && file.Content.Length > _storageOptions.MaxFileSizeBytes)
                ? $"{file.OriginalFileName} exceeds the 25 MB limit."
                : $"{file.OriginalFileName} is empty or invalid.";
        }

        if (string.IsNullOrWhiteSpace(file.Title) || file.Title.Trim().Length > 255)
        {
            return "Title is required.";
        }

        if (!DocumentCategories.IsValid(request.Category))
        {
            return "Category is required.";
        }

        if ((request.Description?.Length ?? 0) > 2000 || (NormalizeTags(request.Tags)?.Length ?? 0) > 500)
        {
            return "Document metadata exceeds the allowed length.";
        }

        if (request.Category == DocumentCategories.PersonalFiles && (request.ProjectId.HasValue || request.TaskId.HasValue))
        {
            return "Personal Files cannot be linked to a project or task.";
        }

        return null;
    }

    private IQueryable<Document> VisibleTo(User user) => _context.Documents.Where(document =>
        user.Role == UserRole.Administrator
        || document.UploadedByUserId == user.UserId
        || (document.ProjectId.HasValue
            && (document.Project!.ProjectManagerId == user.UserId
                || document.Project.ProjectMembers.Any(member => member.UserId == user.UserId)))
        || document.Shares.Any(share =>
            share.SharedWithUserId == user.UserId
            || (user.Department != null
                && share.SharedWithDepartment != null
                && share.SharedWithDepartment.ToLower() == user.Department.ToLower()))
        || (user.Role == UserRole.TeamLead
            && user.Department != null
            && document.Category != DocumentCategories.PersonalFiles
            && document.UploadedBy.Department == user.Department));

    private static bool CanManage(Document document, User user) =>
        user.Role == UserRole.Administrator
        || document.UploadedByUserId == user.UserId
        || (document.Project?.ProjectManagerId == user.UserId)
        || (user.Role == UserRole.TeamLead
            && document.Category != DocumentCategories.PersonalFiles
            && user.Department != null
            && document.UploadedBy?.Department == user.Department);

    private static bool CanReplace(Document document, User user) =>
        user.Role == UserRole.Administrator || document.UploadedByUserId == user.UserId;

    private static bool CanShare(Document document, User user) => document.UploadedByUserId == user.UserId;

    private static DocumentSummary ToSummary(Document document, User user) => new()
    {
        DocumentId = document.DocumentId,
        Title = document.Title,
        Description = document.Description,
        Category = document.Category,
        Tags = (document.Tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        FileType = document.FileType,
        FileSizeBytes = document.FileSizeBytes,
        UploadedDate = document.UploadedDate,
        UploaderName = document.UploadedBy?.DisplayName ?? string.Empty,
        ProjectId = document.ProjectId,
        ProjectName = document.Project?.Name,
        TaskId = document.TaskId,
        CanEdit = CanManage(document, user),
        CanDelete = CanManage(document, user),
        CanReplace = CanReplace(document, user),
        CanShare = CanShare(document, user),
        CanPreview = document.FileExtension is ".pdf" or ".png" or ".jpg" or ".jpeg"
    };

    private async Task LogActivityAsync(Document document, int userId, string action, string? details = null)
    {
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId,
            DocumentTitle = document.Title,
            FileType = document.FileType,
            UserId = userId,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task NotifyProjectUploadAsync(Document document, int uploaderUserId)
    {
        try
        {
            var project = await _context.Projects
                .Include(item => item.ProjectMembers)
                .FirstOrDefaultAsync(item => item.ProjectId == document.ProjectId);
            if (project == null)
            {
                return;
            }

            var recipientIds = project.ProjectMembers
                .Select(member => member.UserId)
                .Append(project.ProjectManagerId)
                .Where(userId => userId != uploaderUserId)
                .Distinct()
                .ToList();
            var recipients = await _context.Users
                .Where(user => recipientIds.Contains(user.UserId) && user.InAppNotificationsEnabled)
                .ToListAsync();

            foreach (var recipient in recipients)
            {
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = recipient.UserId,
                    Type = NotificationType.DocumentAdded,
                    Priority = NotificationPriority.Informational,
                    Title = "New project document",
                    Message = $"{document.Title} was added to {project.Name}."
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to notify project members about document {DocumentId}.", document.DocumentId);
        }
    }

    private static string? NormalizeTags(string? tags)
    {
        var values = (tags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? null : $",{string.Join(',', values)},";
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
