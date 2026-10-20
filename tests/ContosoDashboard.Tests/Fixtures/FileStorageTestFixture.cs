using ContosoDashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Fixtures;

public sealed class FileStorageTestFixture : IAsyncDisposable
{
    public FileStorageTestFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"contoso-document-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentStorage:RootPath"] = RootPath,
                ["DocumentStorage:MaxFileSizeBytes"] = "26214400"
            })
            .Build();
        Storage = new LocalFileStorageService(configuration, new TestHostEnvironment(RootPath), NullLogger<LocalFileStorageService>.Instance);
    }

    public string RootPath { get; }
    public LocalFileStorageService Storage { get; }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "ContosoDashboard.Tests";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}
