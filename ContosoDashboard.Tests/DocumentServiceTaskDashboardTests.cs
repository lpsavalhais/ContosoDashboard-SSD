using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceTaskDashboardTests
{
    [Fact]
    public async Task TaskUploadForcesTaskProjectAndRejectsInaccessibleOrProjectlessTasks()
    {
        await using var database = new TestDb();
        var otherProject = await database.AddProjectAsync(2, "Other Project");
        var accessibleTask = await AddTaskAsync(database.Context, 1, "Project task");
        var inaccessibleTask = await AddTaskAsync(database.Context, otherProject.ProjectId, "Other task");
        var projectlessTask = await AddTaskAsync(database.Context, null, "No project task");
        var service = CreateService(database, new InMemoryFileStorageService());

        var valid = await service.UploadAsync(Request(accessibleTask.TaskId, otherProject.ProjectId), requestingUserId: 4);
        var denied = await service.UploadAsync(Request(inaccessibleTask.TaskId, null), requestingUserId: 4);
        var noProject = await service.UploadAsync(Request(projectlessTask.TaskId, null), requestingUserId: 4);

        Assert.Single(valid.CreatedDocumentIds);
        var saved = await database.Context.Documents.SingleAsync();
        Assert.Equal(1, saved.ProjectId);
        Assert.Equal(accessibleTask.TaskId, saved.TaskId);
        Assert.Empty(denied.CreatedDocumentIds);
        Assert.Contains(denied.Errors, error => error.Message == "Task not found.");
        Assert.Empty(noProject.CreatedDocumentIds);
        Assert.Contains(noProject.Errors, error => error.Message == "This task has no project; upload from My Documents instead.");
    }

    [Fact]
    public async Task TaskDocumentQueryOnlyReturnsDocumentsAttachedToThatTask()
    {
        await using var database = new TestDb();
        var task = await AddTaskAsync(database.Context, 1, "Project task");
        var document = CreateDocument(4, task.TaskId, 1, "Task file");
        await database.AddDocumentAsync(document);
        var otherTask = await AddTaskAsync(database.Context, 1, "Another task");
        await database.AddDocumentAsync(CreateDocument(4, otherTask.TaskId, 1, "Other task file"));
        var service = CreateService(database, new InMemoryFileStorageService());

        var taskDocuments = await service.GetTaskDocumentsAsync(task.TaskId, requestingUserId: 4);

        Assert.Single(taskDocuments);
        Assert.Equal("Task file", taskDocuments[0].Title);
    }

    [Fact]
    public async Task RecentDocumentsAndDashboardCountAreUserSpecific()
    {
        await using var database = new TestDb();
        for (var index = 0; index < 7; index++)
        {
            await database.AddDocumentAsync(CreateDocument(4, null, null, $"Document {index}", DateTime.UtcNow.AddMinutes(index)));
        }
        var service = CreateService(database, new InMemoryFileStorageService());
        var dashboard = new DashboardService(database.Context);

        var recent = await service.GetRecentAsync(4, 5);
        var count = await service.GetMyDocumentCountAsync(4);
        var summary = await dashboard.GetDashboardSummaryAsync(4);

        Assert.Equal(5, recent.Count);
        Assert.Equal("Document 6", recent[0].Title);
        Assert.Equal(7, count);
        Assert.Equal(7, summary.DocumentCount);
    }

    private static async Task<TaskItem> AddTaskAsync(ApplicationDbContext context, int? projectId, string title)
    {
        var task = new TaskItem
        {
            Title = title,
            AssignedUserId = 4,
            CreatedByUserId = 2,
            ProjectId = projectId,
            Status = ContosoDashboard.Models.TaskStatus.NotStarted
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task;
    }

    private static DocumentService CreateService(TestDb database, IFileStorageService storage) => new(
        database.Context,
        storage,
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static UploadBatchRequest Request(int taskId, int? projectId) => new(
        [new FileUpload(new MemoryStream(PdfBytes()), "task.pdf", "application/pdf", 10, "Task file")],
        "Reports", null, null, projectId, taskId);

    private static Document CreateDocument(int userId, int? taskId, int? projectId, string title, DateTime? uploadedDate = null) => new()
    {
        Title = title,
        Category = "Reports",
        OriginalFileName = $"{title}.pdf",
        FilePath = $"{userId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = userId,
        TaskId = taskId,
        ProjectId = projectId,
        UploadedDate = uploadedDate ?? DateTime.UtcNow
    };

    private static byte[] PdfBytes()
    {
        var bytes = new byte[10];
        "%PDF-"u8.CopyTo(bytes);
        return bytes;
    }
}
