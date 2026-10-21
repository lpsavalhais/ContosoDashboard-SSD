using System.Diagnostics;
using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class DocumentServiceUploadTests
{
    [Fact]
    public async Task UploadStoresGeneratedPathAndCanonicalMimeType()
    {
        await using var database = new TestDb();
        var storage = new InMemoryFileStorageService();
        var service = CreateService(database.Context, storage);
        var result = await service.UploadAsync(Request(File("report.pdf", PdfBytes(), "text/html")), requestingUserId: 4);

        var document = await database.Context.Documents.SingleAsync();
        Assert.Single(result.CreatedDocumentIds);
        Assert.Matches(@"^4/personal/[0-9a-f-]{36}\.pdf$", document.FilePath);
        Assert.Equal("application/pdf", document.FileType);
        Assert.Equal("report.pdf", document.OriginalFileName);
        Assert.Single(storage.Files);
        Assert.Equal(DocumentActions.Upload, (await database.Context.DocumentActivities.SingleAsync()).Action);
    }

    [Fact]
    public async Task AcceptsExactLimitAndRejectsOneByteOver()
    {
        await using var database = new TestDb();
        var storage = new InMemoryFileStorageService();
        var service = CreateService(database.Context, storage);
        var exact = PdfBytes(26_214_400);
        var tooLarge = PdfBytes(26_214_401);

        var uploadTimer = Stopwatch.StartNew();
        var result = await service.UploadAsync(Request(
            File("exact.pdf", exact, "application/pdf"),
            File("large.pdf", tooLarge, "application/pdf")), requestingUserId: 4);
        uploadTimer.Stop();

        Assert.Single(result.CreatedDocumentIds);
        Assert.Contains(result.Errors, error => error.FileName == "large.pdf" && error.Message.Contains("25 MB"));
        Assert.Single(await database.Context.Documents.ToListAsync());
        Assert.True(uploadTimer.Elapsed < TimeSpan.FromSeconds(30), $"25 MB upload took {uploadTimer.Elapsed}.");
    }

    [Fact]
    public async Task RejectsInvalidTitleCategoryAndExtension()
    {
        await using var database = new TestDb();
        var service = CreateService(database.Context, new InMemoryFileStorageService());
        var request = new UploadBatchRequest(
        [
            new FileUpload(new MemoryStream(PdfBytes()), "blank.pdf", "application/pdf", 10, " "),
            File("unsupported.exe", PdfBytes(), "application/octet-stream")
        ], "Unknown", null, null, null, null);

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Message == "Title is required.");
        Assert.Contains(result.Errors, error => error.FileName == "unsupported.exe");
        Assert.Empty(await database.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task RejectsInvalidCategory()
    {
        await using var database = new TestDb();
        var service = CreateService(database.Context, new InMemoryFileStorageService());
        var request = new UploadBatchRequest(
            [File("report.pdf", PdfBytes(), "application/pdf")], "Unknown", null, null, null, null);

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Contains(result.Errors, error => error.Message == "Category is required.");
        Assert.Empty(await database.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task ValidFilesSucceedWhenOtherBatchFilesFail()
    {
        await using var database = new TestDb();
        var storage = new InMemoryFileStorageService();
        var service = CreateService(database.Context, storage);
        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var request = Request(
            File("good.pdf", PdfBytes(), "application/pdf"),
            File("eicar.txt", Encoding.ASCII.GetBytes(eicar), "text/plain"),
            File("tool.exe", PdfBytes(), "application/octet-stream"));

        var result = await service.UploadAsync(request, requestingUserId: 4);

        Assert.Single(result.CreatedDocumentIds);
        Assert.Equal(2, result.Errors.Count);
        Assert.Single(await database.Context.Documents.ToListAsync());
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task StorageFailureDoesNotCreateDocumentRecord()
    {
        await using var database = new TestDb();
        var storage = new InMemoryFileStorageService { ThrowOnUpload = true };
        var service = CreateService(database.Context, storage);

        var result = await service.UploadAsync(Request(File("report.pdf", PdfBytes(), "application/pdf")), requestingUserId: 4);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Single(result.Errors);
        Assert.Empty(await database.Context.Documents.ToListAsync());
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task DatabaseFailureDeletesPreviouslyStoredFile()
    {
        await using var database = new TestDb();
        var storage = new InMemoryFileStorageService();
        await using var failingContext = database.CreateFailingSaveContext();
        var service = CreateService(failingContext, storage);

        var result = await service.UploadAsync(Request(File("report.pdf", PdfBytes(), "application/pdf")), requestingUserId: 4);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Single(result.Errors);
        Assert.Empty(storage.Files);
        Assert.Empty(await database.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task RejectsProjectNotAccessibleToUploader()
    {
        await using var database = new TestDb();
        var employee = await database.AddUserAsync("notmember@contoso.com", "Not Member");
        var service = CreateService(database.Context, new InMemoryFileStorageService());

        var result = await service.UploadAsync(
            new UploadBatchRequest([File("report.pdf", PdfBytes(), "application/pdf")], "Reports", null, null, 1, null),
            employee.UserId);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Contains(result.Errors, error => error.Message == "You can only add documents to projects you belong to.");
    }

    [Fact]
    public async Task RejectsPersonalFilesAssociatedWithProjectOrTask()
    {
        await using var database = new TestDb();
        var service = CreateService(database.Context, new InMemoryFileStorageService());

        var result = await service.UploadAsync(
            new UploadBatchRequest([File("private.pdf", PdfBytes(), "application/pdf")], "Personal Files", null, null, 1, null),
            requestingUserId: 4);

        Assert.Empty(result.CreatedDocumentIds);
        Assert.Contains(result.Errors, error => error.Message == "Personal Files cannot be linked to a project or task.");
    }

    private static DocumentService CreateService(ApplicationDbContext context, IFileStorageService storage) => new(
        context,
        storage,
        new LocalMalwareScanner(),
        new NotificationService(context),
        Options.Create(new DocumentStorageOptions()),
        NullLogger<DocumentService>.Instance);

    private static UploadBatchRequest Request(params FileUpload[] files) =>
        new(files.ToList(), "Reports", null, null, null, null);

    private static FileUpload File(string name, byte[] bytes, string contentType) =>
        new(new MemoryStream(bytes), name, contentType, bytes.Length, Path.GetFileNameWithoutExtension(name));

    private static byte[] PdfBytes(int size = 10)
    {
        var bytes = Enumerable.Repeat((byte)'A', size).ToArray();
        "%PDF-"u8.CopyTo(bytes);
        return bytes;
    }
}
