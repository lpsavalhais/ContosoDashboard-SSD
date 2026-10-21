using System.Diagnostics;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceSearchTests
{
    [Fact]
    public async Task SearchesTitleDescriptionTagUploaderAndProjectName()
    {
        await using var database = new TestDb();
        var document = CreateDocument(4, projectId: 1, "Quarterly Results", "budget forecast", ",budget,q3,");
        await database.AddDocumentAsync(document);
        var service = CreateService(database);

        Assert.Single(await service.SearchAsync("Quarterly", requestingUserId: 4));
        Assert.Single(await service.SearchAsync("forecast", requestingUserId: 4));
        Assert.Single(await service.SearchAsync("budget", requestingUserId: 4));
        Assert.Single(await service.SearchAsync("Ni Kang", requestingUserId: 2));
        Assert.Single(await service.SearchAsync("ContosoDashboard Development", requestingUserId: 3));
    }

    [Fact]
    public async Task SearchReturnsOnlyVisibleDocuments()
    {
        await using var database = new TestDb();
        await database.AddDocumentAsync(CreateDocument(4, projectId: 1, "Project budget"));
        await database.AddDocumentAsync(CreateDocument(4, projectId: null, "Personal budget", category: DocumentCategories.PersonalFiles));
        var outsider = await database.AddUserAsync("search-outsider@contoso.com", "Search Outsider", department: "Marketing");
        var service = CreateService(database);

        var teamLeadResults = await service.SearchAsync("budget", requestingUserId: 3);
        var outsiderResults = await service.SearchAsync("budget", outsider.UserId);

        Assert.Single(teamLeadResults);
        Assert.Equal("Project budget", teamLeadResults[0].Title);
        Assert.Empty(outsiderResults);
    }

    [Theory]
    [InlineData("100% complete", "%")]
    [InlineData("file_name", "_")]
    public async Task TreatsSqlWildcardsAsLiteralCharacters(string title, string term)
    {
        await using var database = new TestDb();
        await database.AddDocumentAsync(CreateDocument(4, projectId: null, title));
        await database.AddDocumentAsync(CreateDocument(4, projectId: null, "1000 complete fileXname"));
        var service = CreateService(database);

        var results = await service.SearchAsync(term, requestingUserId: 4);

        Assert.Single(results);
        Assert.Equal(title, results[0].Title);
    }

    [Fact]
    public async Task BlankSearchReturnsNoResults()
    {
        await using var database = new TestDb();

        var results = await CreateService(database).SearchAsync("  ", requestingUserId: 4);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ListsAndSearchesFiveHundredDocumentsWithinTwoSeconds()
    {
        await using var database = new TestDb();
        database.Context.Documents.AddRange(Enumerable.Range(0, 500).Select(index => CreateDocument(
            4,
            projectId: null,
            $"Performance document {index:D3}",
            tags: ",perf-marker,")));
        await database.Context.SaveChangesAsync();
        var service = CreateService(database);

        var listTimer = Stopwatch.StartNew();
        var list = await service.GetMyDocumentsAsync(4, new DocumentQuery());
        listTimer.Stop();

        var searchTimer = Stopwatch.StartNew();
        var search = await service.SearchAsync("perf-marker", 4);
        searchTimer.Stop();

        Assert.Equal(500, list.Count);
        Assert.Equal(500, search.Count);
        Assert.True(listTimer.Elapsed < TimeSpan.FromSeconds(2), $"List took {listTimer.Elapsed}.");
        Assert.True(searchTimer.Elapsed < TimeSpan.FromSeconds(2), $"Search took {searchTimer.Elapsed}.");
    }

    private static DocumentService CreateService(TestDb database) => new(
        database.Context,
        new InMemoryFileStorageService(),
        new LocalMalwareScanner(),
        new NotificationService(database.Context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static Document CreateDocument(
        int uploadedByUserId,
        int? projectId,
        string title,
        string? description = null,
        string? tags = null,
        string category = "Reports") => new()
    {
        Title = title,
        Description = description,
        Category = category,
        Tags = tags,
        OriginalFileName = "search.pdf",
        FilePath = $"{uploadedByUserId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():D}.pdf",
        FileType = "application/pdf",
        FileExtension = ".pdf",
        FileSizeBytes = 10,
        UploadedByUserId = uploadedByUserId,
        ProjectId = projectId
    };
}
