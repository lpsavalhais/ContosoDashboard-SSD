using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests;

public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Context = new ApplicationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public ApplicationDbContext Context { get; }

    public ApplicationDbContext CreateFailingSaveContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FailingSaveDbContext(options);
    }

    public async Task<User> AddUserAsync(
        string email,
        string displayName,
        UserRole role = UserRole.Employee,
        string? department = "Engineering")
    {
        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            Role = role,
            Department = department
        };
        Context.Users.Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    public async Task<Project> AddProjectAsync(int projectManagerId, string name = "Test Project")
    {
        var project = new Project
        {
            Name = name,
            ProjectManagerId = projectManagerId,
            StartDate = DateTime.UtcNow,
            Status = ProjectStatus.Active
        };
        Context.Projects.Add(project);
        await Context.SaveChangesAsync();
        return project;
    }

    public async Task<ProjectMember> AddProjectMemberAsync(int projectId, int userId)
    {
        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = "TeamMember"
        };
        Context.ProjectMembers.Add(member);
        await Context.SaveChangesAsync();
        return member;
    }

    public async Task<Document> AddDocumentAsync(Document document)
    {
        Context.Documents.Add(document);
        await Context.SaveChangesAsync();
        return document;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class FailingSaveDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated database failure.");
    }
}

public sealed class InMemoryFileStorageService : IFileStorageService
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public bool ThrowOnUpload { get; set; }

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
    {
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer);
        Files.Add(storageKey, buffer.ToArray());
        if (ThrowOnUpload)
        {
            throw new IOException("Simulated storage failure after a partial write.");
        }

        return storageKey;
    }

    public Task<Stream> DownloadAsync(string storageKey)
    {
        if (!Files.TryGetValue(storageKey, out var bytes))
        {
            throw new FileNotFoundException("Stored document was not found.", storageKey);
        }

        Stream stream = new MemoryStream(bytes, writable: false);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey)
    {
        Files.Remove(storageKey);
        return Task.CompletedTask;
    }

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration) => Task.FromResult(storageKey);
}
