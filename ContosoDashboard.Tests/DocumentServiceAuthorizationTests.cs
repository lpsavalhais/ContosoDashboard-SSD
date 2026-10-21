using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceAuthorizationTests
{
    [Fact]
    public async Task AppliesVisibilityAndManagementRulesForProjectDocument()
    {
        await using var database = new TestDb();
        var manager = await database.Context.Users.SingleAsync(user => user.UserId == 2);
        var owner = await database.Context.Users.SingleAsync(user => user.UserId == 4);
        var teamLead = await database.Context.Users.SingleAsync(user => user.UserId == 3);
        var directRecipient = await database.AddUserAsync("direct@contoso.com", "Direct Recipient", department: "Sales");
        var departmentRecipient = await database.AddUserAsync("department@contoso.com", "Department Recipient", department: "Sales");
        var outsider = await database.AddUserAsync("outsider@contoso.com", "Outsider", department: "Marketing");
        var document = await database.AddDocumentAsync(CreateDocument(owner.UserId, projectId: 1, "Reports"));
        database.Context.DocumentShares.AddRange(
            new DocumentShare { DocumentId = document.DocumentId, SharedWithUserId = directRecipient.UserId, SharedByUserId = owner.UserId },
            new DocumentShare { DocumentId = document.DocumentId, SharedWithDepartment = "Sales", SharedByUserId = owner.UserId });
        await database.Context.SaveChangesAsync();

        var admin = await GetDocumentAsync(database, document.DocumentId, requestingUserId: 1);
        var ownerSummary = await GetDocumentAsync(database, document.DocumentId, owner.UserId);
        var managerSummary = await GetDocumentAsync(database, document.DocumentId, manager.UserId);
        var leadSummary = await GetDocumentAsync(database, document.DocumentId, teamLead.UserId);
        var directSummary = await GetDocumentAsync(database, document.DocumentId, directRecipient.UserId);
        var departmentSummary = await GetDocumentAsync(database, document.DocumentId, departmentRecipient.UserId);
        var denied = await GetDocumentAsync(database, document.DocumentId, outsider.UserId);

        Assert.True(admin!.CanEdit);
        Assert.True(admin.CanDelete);
        Assert.True(admin.CanReplace);
        Assert.False(admin.CanShare);
        Assert.True(ownerSummary!.CanEdit);
        Assert.True(ownerSummary.CanDelete);
        Assert.True(ownerSummary.CanReplace);
        Assert.True(ownerSummary.CanShare);
        Assert.True(managerSummary!.CanEdit);
        Assert.True(managerSummary.CanDelete);
        Assert.False(managerSummary.CanReplace);
        Assert.False(managerSummary.CanShare);
        Assert.True(leadSummary!.CanEdit);
        Assert.True(leadSummary.CanDelete);
        Assert.False(leadSummary.CanReplace);
        Assert.False(leadSummary.CanShare);
        Assert.False(directSummary!.CanEdit);
        Assert.False(directSummary.CanDelete);
        Assert.False(directSummary.CanReplace);
        Assert.False(directSummary.CanShare);
        Assert.NotNull(departmentSummary);
        Assert.Null(denied);
    }

    [Fact]
    public async Task PersonalFilesAreHiddenFromTeamLeadButVisibleToOwnerAndAdministrator()
    {
        await using var database = new TestDb();
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: null, DocumentCategories.PersonalFiles));
        var service = CreateService(database, new InMemoryFileStorageService());

        var owner = await service.GetByIdAsync(document.DocumentId, requestingUserId: 4);
        var teamLead = await service.GetByIdAsync(document.DocumentId, requestingUserId: 3);
        var admin = await service.GetByIdAsync(document.DocumentId, requestingUserId: 1);

        Assert.NotNull(owner);
        Assert.Null(teamLead);
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task OpenForReadRequiresVisibilityAndAuditsAuthorizedDownload()
    {
        await using var database = new TestDb();
        var outsider = await database.AddUserAsync("download-outsider@contoso.com", "Download Outsider", department: "Marketing");
        var document = await database.AddDocumentAsync(CreateDocument(4, projectId: 1, "Reports"));
        var storage = new InMemoryFileStorageService();
        storage.Files.Add(document.FilePath, [1, 2, 3]);
        var service = CreateService(database, storage);

        var denied = await service.OpenForReadAsync(document.DocumentId, outsider.UserId, isPreview: false);
        var allowed = await service.OpenForReadAsync(document.DocumentId, requestingUserId: 4, isPreview: false);

        Assert.Null(denied);
        Assert.NotNull(allowed);
        Assert.Equal("application/pdf", allowed.ContentType);
        Assert.Equal("test.pdf", allowed.FileName);
        Assert.Equal(DocumentActions.Download, (await database.Context.DocumentActivities.SingleAsync()).Action);
    }

    [Fact]
    public async Task PreviewRejectsNonPreviewableFileType()
    {
        await using var database = new TestDb();
        var document = CreateDocument(4, projectId: null, "Reports");
        document.FileExtension = ".docx";
        document.FileType = DocumentFileTypes.Map[".docx"];
        document.OriginalFileName = "report.docx";
        document.FilePath = $"4/personal/{Guid.NewGuid():D}.docx";
        await database.AddDocumentAsync(document);

        var result = await CreateService(database, new InMemoryFileStorageService())
            .OpenForReadAsync(document.DocumentId, requestingUserId: 4, isPreview: true);

        Assert.Null(result);
        Assert.Empty(await database.Context.DocumentActivities.ToListAsync());
    }

    private static async Task<DocumentSummary?> GetDocumentAsync(TestDb database, int documentId, int requestingUserId) =>
        await CreateService(database, new InMemoryFileStorageService()).GetByIdAsync(documentId, requestingUserId);

    private static DocumentService CreateService(TestDb database, IFileStorageService storage) => new(
        database.Context,
        storage,
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static Document CreateDocument(int userId, int? projectId, string category) => new()
    {
        Title = "Test document",
        Category = category,
        OriginalFileName = "test.pdf",
        FilePath = $"{userId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = userId,
        ProjectId = projectId
    };
}
