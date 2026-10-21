using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IWebHostEnvironment environment, IOptions<DocumentStorageOptions> options)
    {
        var configuredRoot = options.Value.RootPath;
        _rootPath = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType)
    {
        var fullPath = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var destination = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await fileStream.CopyToAsync(destination);
        return storageKey;
    }

    public Task<Stream> DownloadAsync(string storageKey)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored document was not found.", storageKey);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration)
    {
        return Task.FromResult(storageKey);
    }

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new InvalidOperationException("Storage key is required.");
        }

        var normalizedKey = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedKey))
        {
            throw new InvalidOperationException("Storage key must be relative to the storage root.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
        if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Storage key resolves outside the storage root.");
        }

        return fullPath;
    }
}
