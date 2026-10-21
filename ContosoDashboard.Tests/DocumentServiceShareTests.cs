using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceShareTests
{
    [Fact]
    public async Task SharesWithUsersAndDepartmentsWithoutDuplicateRowsOrNotifications()
    {
        await using var database = new TestDb();
        var directRecipient = await database.AddUserAsync("share-direct@contoso.com", "Direct Recipient", department: "Sales");
        var laterRecipient = await database.AddUserAsync("share-later@contoso.com", "Later Recipient", department: "Engineering");
        var document = await database.AddDocumentAsync(CreateDocument(4));
        var service = CreateService(database);
        var request = new ShareRequest([directRecipient.UserId, directRecipient.UserId], ["engineering", "Engineering"]);

        var result = await service.ShareAsync(document.DocumentId, request, requestingUserId: 4);
        var repeated = await service.ShareAsync(document.DocumentId, request, requestingUserId: 4);

        Assert.True(result.Success);
        Assert.True(repeated.Success);
        Assert.Equal(2, await database.Context.DocumentShares.CountAsync());
        var notifications = await database.Context.Notifications.OrderBy(item => item.UserId).ToListAsync();
        Assert.Equal(new[] { 2, directRecipient.UserId, laterRecipient.UserId }, notifications.Select(item => item.UserId).Order().ToArray());
        Assert.DoesNotContain(notifications, item => item.UserId == 4);
        Assert.All(notifications, item => Assert.Equal(NotificationType.DocumentShared, item.Type));

        var laterRecipientDocument = await service.GetByIdAsync(document.DocumentId, laterRecipient.UserId);
        Assert.NotNull(laterRecipientDocument);
    }

    [Fact]
    public async Task OnlyUploaderCanShareAndSharedAccessIsReadOnly()
    {
        await using var database = new TestDb();
        var recipient = await database.AddUserAsync("share-readonly@contoso.com", "Read Only", department: "Sales");
        var document = await database.AddDocumentAsync(CreateDocument(4));
        var service = CreateService(database);
        var request = new ShareRequest([recipient.UserId], []);

        var deniedShare = await service.ShareAsync(document.DocumentId, request, requestingUserId: 3);
        var shared = await service.ShareAsync(document.DocumentId, request, requestingUserId: 4);
        var summary = await service.GetByIdAsync(document.DocumentId, recipient.UserId);
        var update = await service.UpdateMetadataAsync(document.DocumentId, new DocumentMetadataUpdate("Changed", null, "Reports", null), recipient.UserId);
        var delete = await service.DeleteAsync(document.DocumentId, recipient.UserId);
        var reShare = await service.ShareAsync(document.DocumentId, new ShareRequest([], ["Sales"]), recipient.UserId);
        var replacement = await service.ReplaceFileAsync(
            document.DocumentId,
            new FileUpload(new MemoryStream(PdfBytes()), "replacement.pdf", "application/pdf", 10, "Replacement"),
            recipient.UserId);

        Assert.False(deniedShare.Success);
        Assert.True(shared.Success);
        Assert.NotNull(summary);
        Assert.False(summary.CanEdit);
        Assert.False(summary.CanDelete);
        Assert.False(summary.CanReplace);
        Assert.False(summary.CanShare);
        Assert.False(update);
        Assert.False(delete);
        Assert.False(reShare.Success);
        Assert.False(replacement.Success);
    }

    [Fact]
    public async Task DisabledNotificationRecipientStillGetsDepartmentAccess()
    {
        await using var database = new TestDb();
        var optedOut = await database.AddUserAsync("share-optout@contoso.com", "Share Opt Out", department: "Sales");
        optedOut.InAppNotificationsEnabled = false;
        await database.Context.SaveChangesAsync();
        var document = await database.AddDocumentAsync(CreateDocument(4));
        var service = CreateService(database);

        var result = await service.ShareAsync(document.DocumentId, new ShareRequest([], ["Sales"]), requestingUserId: 4);

        Assert.True(result.Success);
        Assert.DoesNotContain(await database.Context.Notifications.ToListAsync(), item => item.UserId == optedOut.UserId);
        Assert.NotNull(await service.GetByIdAsync(document.DocumentId, optedOut.UserId));
    }

    [Fact]
    public async Task SharedWithMeIncludesDirectAndDepartmentSharesButNotOwnUploads()
    {
        await using var database = new TestDb();
        var recipient = await database.AddUserAsync("shared-list@contoso.com", "Shared List", department: "Sales");
        var direct = await database.AddDocumentAsync(CreateDocument(2, "Direct share"));
        var department = await database.AddDocumentAsync(CreateDocument(3, "Department share"));
        await database.AddDocumentAsync(CreateDocument(recipient.UserId, "Own document"));
        database.Context.DocumentShares.AddRange(
            new DocumentShare { DocumentId = direct.DocumentId, SharedWithUserId = recipient.UserId, SharedByUserId = 2 },
            new DocumentShare { DocumentId = department.DocumentId, SharedWithDepartment = "Sales", SharedByUserId = 3 });
        await database.Context.SaveChangesAsync();

        var results = await CreateService(database).GetSharedWithMeAsync(recipient.UserId);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, item => item.Title == "Direct share");
        Assert.Contains(results, item => item.Title == "Department share");
        Assert.DoesNotContain(results, item => item.Title == "Own document");
    }

    private static DocumentService CreateService(TestDb database) => new(
        database.Context,
        new InMemoryFileStorageService(),
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static Document CreateDocument(int uploadedByUserId, string title = "Shared document") => new()
    {
        Title = title,
        Category = "Reports",
        OriginalFileName = "shared.pdf",
        FilePath = $"{uploadedByUserId}/personal/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = uploadedByUserId
    };

    private static byte[] PdfBytes()
    {
        var bytes = new byte[10];
        "%PDF-"u8.CopyTo(bytes);
        return bytes;
    }
}
