using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests;

public class DocumentReportServiceTests
{
    [Fact]
    public async Task NonAdministratorCannotReadReports()
    {
        await using var database = new TestDb();
        var service = new DocumentReportService(database.Context);

        var report = await service.GetReportAsync(4, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        Assert.Null(report);
    }

    [Fact]
    public async Task AdministratorReportsUploadsAndAccessIncludingDeletedDocuments()
    {
        await using var database = new TestDb();
        var now = DateTime.UtcNow;
        database.Context.DocumentActivities.AddRange(
            Activity(100, "Deleted report", "application/pdf", 4, DocumentActions.Upload, now.AddDays(-1)),
            Activity(100, "Deleted report", "application/pdf", 4, DocumentActions.Download, now.AddDays(-1)),
            Activity(100, "Deleted report", "application/pdf", 4, DocumentActions.Preview, now.AddDays(-1)),
            Activity(101, "Sheet", "application/vnd.ms-excel", 2, DocumentActions.Upload, now.AddDays(-2)),
            Activity(102, "Another report", "application/pdf", 4, DocumentActions.Upload, now.AddDays(-3)),
            Activity(999, "Outside range", "application/pdf", 4, DocumentActions.Download, now.AddDays(-60)));
        await database.Context.SaveChangesAsync();
        var service = new DocumentReportService(database.Context);

        var report = await service.GetReportAsync(1, now.AddDays(-30), now);

        Assert.NotNull(report);
        Assert.Contains(report.TopFileTypes, row => row.FileType == "application/pdf" && row.UploadCount == 2);
        Assert.Contains(report.TopUploaders, row => row.UserName == "Ni Kang" && row.UploadCount == 2);
        Assert.Contains(report.AccessByDocument, row => row.Title == "Deleted report" && row.Downloads == 1 && row.Previews == 1);
        Assert.DoesNotContain(report.AccessByDocument, row => row.Title == "Outside range");
        Assert.Contains(report.AccessByDay, row => row.Date == now.AddDays(-1).Date && row.Downloads == 1 && row.Previews == 1);
    }

    private static DocumentActivity Activity(int documentId, string title, string type, int userId, string action, DateTime timestamp) => new()
    {
        DocumentId = documentId,
        DocumentTitle = title,
        FileType = type,
        UserId = userId,
        Action = action,
        Timestamp = timestamp
    };
}
