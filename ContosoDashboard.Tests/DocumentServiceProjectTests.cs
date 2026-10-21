using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceProjectTests
{
    [Fact]
    public async Task ProjectDocumentsAreVisibleOnlyToProjectMembersAndManager()
    {
        await using var database = new TestDb();
        var outsider = await database.AddUserAsync("project-outsider@contoso.com", "Project Outsider", department: "Sales");
        await database.AddDocumentAsync(CreateDocument(uploadedByUserId: 4, projectId: 1));
        var service = CreateService(database);

        var memberDocuments = await service.GetProjectDocumentsAsync(1, requestingUserId: 4);
        var managerDocuments = await service.GetProjectDocumentsAsync(1, requestingUserId: 2);
        var outsiderDocuments = await service.GetProjectDocumentsAsync(1, outsider.UserId);

        Assert.Single(memberDocuments);
        Assert.Single(managerDocuments);
        Assert.Empty(outsiderDocuments);
    }

    [Fact]
    public async Task ProjectUploadNotifiesManagerAndMembersExceptUploader()
    {
        await using var database = new TestDb();
        var service = CreateService(database);
        var request = new UploadBatchRequest(
            [new FileUpload(new MemoryStream(PdfBytes()), "project.pdf", "application/pdf", 10, "Project file")],
            "Project Documents", null, null, 1, null);

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Single(result.CreatedDocumentIds);
        var notifications = await database.Context.Notifications.OrderBy(item => item.UserId).ToListAsync();
        Assert.Equal([2, 3], notifications.Select(item => item.UserId));
        Assert.All(notifications, item => Assert.Equal(NotificationType.DocumentAdded, item.Type));
    }

    [Fact]
    public async Task ProjectUploadSkipsUsersWithNotificationsDisabled()
    {
        await using var database = new TestDb();
        var optedOut = await database.AddUserAsync("optedout@contoso.com", "Opted Out");
        optedOut.InAppNotificationsEnabled = false;
        await database.Context.SaveChangesAsync();
        await database.AddProjectMemberAsync(1, optedOut.UserId);
        var service = CreateService(database);
        var request = new UploadBatchRequest(
            [new FileUpload(new MemoryStream(PdfBytes()), "project.pdf", "application/pdf", 10, "Project file")],
            "Project Documents", null, null, 1, null);

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Single(result.CreatedDocumentIds);
        Assert.DoesNotContain(await database.Context.Notifications.ToListAsync(), item => item.UserId == optedOut.UserId);
        Assert.Single(await service.GetProjectDocumentsAsync(1, optedOut.UserId));
    }

    [Fact]
    public async Task PersonalUploadDoesNotCreateProjectNotifications()
    {
        await using var database = new TestDb();
        var service = CreateService(database);
        var request = new UploadBatchRequest(
            [new FileUpload(new MemoryStream(PdfBytes()), "personal.pdf", "application/pdf", 10, "Personal file")],
            "Personal Files", null, null, null, null);

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Single(result.CreatedDocumentIds);
        Assert.Empty(await database.Context.Notifications.ToListAsync());
    }

    private static DocumentService CreateService(TestDb database) => new(
        database.Context,
        new InMemoryFileStorageService(),
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static Document CreateDocument(int uploadedByUserId, int projectId) => new()
    {
        Title = "Project document",
        Category = DocumentCategories.ProjectDocuments,
        OriginalFileName = "project.pdf",
        FilePath = $"{uploadedByUserId}/{projectId}/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = uploadedByUserId,
        ProjectId = projectId
    };

    private static byte[] PdfBytes()
    {
        var bytes = new byte[10];
        "%PDF-"u8.CopyTo(bytes);
        return bytes;
    }
}
