using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public interface IDocumentReportService
{
    Task<DocumentReport?> GetReportAsync(int requestingUserId, DateTime fromUtc, DateTime toUtc);
}

public sealed class DocumentReport
{
    public List<FileTypeUploadReport> TopFileTypes { get; init; } = [];
    public List<UploaderReport> TopUploaders { get; init; } = [];
    public List<DocumentAccessReport> AccessByDocument { get; init; } = [];
    public List<DailyDocumentAccessReport> AccessByDay { get; init; } = [];
}

public sealed record FileTypeUploadReport(string FileType, int UploadCount);
public sealed record UploaderReport(string UserName, int UploadCount);
public sealed record DocumentAccessReport(int DocumentId, string Title, int Downloads, int Previews);
public sealed record DailyDocumentAccessReport(DateTime Date, int Downloads, int Previews);

public sealed class DocumentReportService : IDocumentReportService
{
    private readonly ApplicationDbContext _context;

    public DocumentReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentReport?> GetReportAsync(int requestingUserId, DateTime fromUtc, DateTime toUtc)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == requestingUserId);
        if (user?.Role != UserRole.Administrator)
        {
            return null;
        }

        var activities = _context.DocumentActivities.AsNoTracking()
            .Where(activity => activity.Timestamp >= fromUtc && activity.Timestamp <= toUtc);

        var fileTypeRows = await activities
            .Where(activity => activity.Action == DocumentActions.Upload)
            .GroupBy(activity => activity.FileType)
            .Select(group => new { FileType = group.Key, UploadCount = group.Count() })
            .OrderByDescending(row => row.UploadCount)
            .ThenBy(row => row.FileType)
            .Take(10)
            .ToListAsync();

        var uploaderRows = await (
            from activity in activities
            join actor in _context.Users.AsNoTracking() on activity.UserId equals actor.UserId
            where activity.Action == DocumentActions.Upload
            group activity by actor.DisplayName into grouped
            select new { UserName = grouped.Key, UploadCount = grouped.Count() })
            .OrderByDescending(row => row.UploadCount)
            .ThenBy(row => row.UserName)
            .Take(10)
            .ToListAsync();

        var documentAccessRows = await activities
            .Where(activity => activity.Action == DocumentActions.Download || activity.Action == DocumentActions.Preview)
            .GroupBy(activity => new { activity.DocumentId, activity.DocumentTitle })
            .Select(group => new
            {
                group.Key.DocumentId,
                Title = group.Key.DocumentTitle,
                Downloads = group.Count(activity => activity.Action == DocumentActions.Download),
                Previews = group.Count(activity => activity.Action == DocumentActions.Preview)
            })
            .OrderByDescending(row => row.Downloads + row.Previews)
            .ThenBy(row => row.Title)
            .Take(10)
            .ToListAsync();

        var dayAccessRows = await activities
            .Where(activity => activity.Action == DocumentActions.Download || activity.Action == DocumentActions.Preview)
            .GroupBy(activity => activity.Timestamp.Date)
            .Select(group => new
            {
                Date = group.Key,
                Downloads = group.Count(activity => activity.Action == DocumentActions.Download),
                Previews = group.Count(activity => activity.Action == DocumentActions.Preview)
            })
            .OrderBy(row => row.Date)
            .ToListAsync();

        return new DocumentReport
        {
            TopFileTypes = fileTypeRows.Select(row => new FileTypeUploadReport(row.FileType, row.UploadCount)).ToList(),
            TopUploaders = uploaderRows.Select(row => new UploaderReport(row.UserName, row.UploadCount)).ToList(),
            AccessByDocument = documentAccessRows.Select(row => new DocumentAccessReport(row.DocumentId, row.Title, row.Downloads, row.Previews)).ToList(),
            AccessByDay = dayAccessRows.Select(row => new DailyDocumentAccessReport(row.Date, row.Downloads, row.Previews)).ToList()
        };
    }
}
