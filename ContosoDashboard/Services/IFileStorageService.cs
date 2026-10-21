namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType);
    Task<Stream> DownloadAsync(string storageKey);
    Task DeleteAsync(string storageKey);
    Task<string> GetUrlAsync(string storageKey, TimeSpan expiration);
}

public sealed class DocumentStorageOptions
{
    public string RootPath { get; set; } = "AppData/uploads";
    public long MaxFileSizeBytes { get; set; } = 26_214_400;
}
