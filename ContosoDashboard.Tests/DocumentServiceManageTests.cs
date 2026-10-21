using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceManageTests
{
    [Fact]
    public async Task MetadataEditsFollowDocumentManagementPermissions()
    {
        await using var database = new TestDb();
        var recipient = await database.AddUserAsync("share-edit@contoso.com", "Share Edit", department: "Sales");
        var member = await database.AddUserAsync("project-member@contoso.com", "Project Member", department: "Sales");
        await database.AddProjectMemberAsync(1, member.UserId);
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: 1, "Reports"));
        database.Context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = document.DocumentId,
            SharedWithUserId = recipient.UserId,
            SharedByUserId = 4
        });
        await database.Context.SaveChangesAsync();
        var service = CreateService(database, new InMemoryFileStorageService());
        var update = new DocumentMetadataUpdate("Updated title", "Updated description", "Reports", "budget, q3");

        Assert.True(await service.UpdateMetadataAsync(document.DocumentId, update, requestingUserId: 4));
        Assert.True(await service.UpdateMetadataAsync(document.DocumentId, update, requestingUserId: 2));
        Assert.True(await service.UpdateMetadataAsync(document.DocumentId, update, requestingUserId: 3));
        Assert.True(await service.UpdateMetadataAsync(document.DocumentId, update, requestingUserId: 1));
        Assert.False(await service.UpdateMetadataAsync(document.DocumentId, update, member.UserId));
        Assert.False(await service.UpdateMetadataAsync(document.DocumentId, update, recipient.UserId));

        var personalDocument = await database.AddDocumentAsync(CreateDocument(4, projectId: null, DocumentCategories.PersonalFiles));
        Assert.False(await service.UpdateMetadataAsync(
            personalDocument.DocumentId,
            new DocumentMetadataUpdate("Private", null, "Reports", null),
            requestingUserId: 3));
    }

    [Fact]
    public async Task MetadataValidationRejectsBlankTitleInvalidCategoryAndLinkedPersonalFiles()
    {
        await using var database = new TestDb();
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: 1, "Reports"));
        var service = CreateService(database, new InMemoryFileStorageService());

        Assert.False(await service.UpdateMetadataAsync(document.DocumentId, new DocumentMetadataUpdate(" ", null, "Reports", null), 4));
        Assert.False(await service.UpdateMetadataAsync(document.DocumentId, new DocumentMetadataUpdate("Title", null, "Unknown", null), 4));
        Assert.False(await service.UpdateMetadataAsync(document.DocumentId, new DocumentMetadataUpdate("Title", null, "Personal Files", null), 4));
        Assert.Equal("Reports", (await database.Context.Documents.SingleAsync()).Category);
    }

    [Fact]
    public async Task ReplaceStoresNewFileAndDeletesOldOnlyForUploaderOrAdministrator()
    {
        await using var database = new TestDb();
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: null, "Reports"));
        var storage = new InMemoryFileStorageService();
        storage.Files.Add(document.FilePath, PdfBytes(10));
        var service = CreateService(database, storage);
        var replacement = new FileUpload(new MemoryStream(PdfBytes(20)), "new.pdf", "text/html", 20, "Ignored title");

        var denied = await service.ReplaceFileAsync(document.DocumentId, replacement, requestingUserId: 3);
        var result = await service.ReplaceFileAsync(document.DocumentId, replacement, requestingUserId: 4);
        var adminResult = await service.ReplaceFileAsync(document.DocumentId, replacement, requestingUserId: 1);
        var updated = await database.Context.Documents.SingleAsync();

        Assert.False(denied.Success);
        Assert.True(result.Success);
        Assert.True(adminResult.Success);
        Assert.NotEqual("4/personal/original.pdf", updated.FilePath);
        Assert.Equal("application/pdf", updated.FileType);
        Assert.Equal("new.pdf", updated.OriginalFileName);
        Assert.Equal(20, updated.FileSizeBytes);
        Assert.Single(storage.Files);
        var activities = await database.Context.DocumentActivities.ToListAsync();
        Assert.Equal(2, activities.Count);
        Assert.All(activities, activity => Assert.Equal(DocumentActions.Replace, activity.Action));
    }

    [Fact]
    public async Task ReplaceRejectsOversizedAndEicarFiles()
    {
        await using var database = new TestDb();
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: null, "Reports"));
        var storage = new InMemoryFileStorageService();
        storage.Files.Add(document.FilePath, PdfBytes(10));
        var service = CreateService(database, storage);
        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

        var tooLarge = await service.ReplaceFileAsync(
            document.DocumentId,
            new FileUpload(new MemoryStream(PdfBytes(26_214_401)), "large.pdf", "application/pdf", 26_214_401, "Large"),
            requestingUserId: 4);
        var infected = await service.ReplaceFileAsync(
            document.DocumentId,
            new FileUpload(new MemoryStream(Encoding.ASCII.GetBytes(eicar)), "bad.txt", "text/plain", eicar.Length, "Bad"),
            requestingUserId: 4);

        Assert.False(tooLarge.Success);
        Assert.False(infected.Success);
        Assert.Single(storage.Files);
        Assert.Equal("original.pdf", (await database.Context.Documents.SingleAsync()).OriginalFileName);
    }

    [Fact]
    public async Task ProjectManagerCanDeleteProjectDocumentAndSharesCascade()
    {
        await using var database = new TestDb();
        var outsider = await database.AddUserAsync("delete-outsider@contoso.com", "Delete Outsider", department: "Sales");
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: 1, "Reports"));
        var recipient = await database.AddUserAsync("delete-share@contoso.com", "Delete Share", department: "Sales");
        database.Context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = document.DocumentId,
            SharedWithUserId = recipient.UserId,
            SharedByUserId = 4
        });
        await database.Context.SaveChangesAsync();
        var storage = new InMemoryFileStorageService();
        storage.Files.Add(document.FilePath, PdfBytes(10));
        var service = CreateService(database, storage);

        Assert.False(await service.DeleteAsync(document.DocumentId, outsider.UserId));
        Assert.True(await service.DeleteAsync(document.DocumentId, requestingUserId: 2));
        Assert.Empty(await database.Context.Documents.ToListAsync());
        Assert.Empty(await database.Context.DocumentShares.ToListAsync());
        Assert.Empty(storage.Files);
        Assert.Equal(DocumentActions.Delete, (await database.Context.DocumentActivities.SingleAsync()).Action);
    }

    private static DocumentService CreateService(TestDb database, IFileStorageService storage) => new(
        database.Context,
        storage,
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static Document CreateDocument(int uploadedByUserId, int? projectId, string category) => new()
    {
        Title = "Original title",
        Description = "Original description",
        Category = category,
        OriginalFileName = "original.pdf",
        FilePath = $"{uploadedByUserId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = uploadedByUserId,
        ProjectId = projectId
    };

    private static byte[] PdfBytes(int size)
    {
        var bytes = Enumerable.Repeat((byte)'A', size).ToArray();
        "%PDF-"u8.CopyTo(bytes);
        return bytes;
    }
}
