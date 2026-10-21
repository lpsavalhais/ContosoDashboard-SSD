using ContosoDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

public class LocalFileStorageServiceTests
{
    [Fact]
    public async Task UploadDownloadDeleteRoundTrip()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = CreateService(root);
            var expected = new byte[] { 1, 2, 3, 4 };
            var key = "4/personal/test.pdf";

            await service.UploadAsync(new MemoryStream(expected), key, "application/pdf");
            await using var actualStream = await service.DownloadAsync(key);
            using var actual = new MemoryStream();
            await actualStream.CopyToAsync(actual);

            Assert.Equal(expected, actual.ToArray());
            await service.DeleteAsync(key);
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.DownloadAsync(key));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteMissingFileIsNoOp()
    {
        var root = CreateTempDirectory();
        try
        {
            await CreateService(root).DeleteAsync("4/personal/missing.pdf");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/tmp/outside.txt")]
    public async Task RejectsPathsOutsideStorageRoot(string key)
    {
        var root = CreateTempDirectory();
        try
        {
            var service = CreateService(root);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(key));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalFileStorageService CreateService(string root) => new(
        new TestWebHostEnvironment(root),
        Options.Create(new DocumentStorageOptions { RootPath = "uploads" }));

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"contoso-docs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "ContosoDashboard.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
