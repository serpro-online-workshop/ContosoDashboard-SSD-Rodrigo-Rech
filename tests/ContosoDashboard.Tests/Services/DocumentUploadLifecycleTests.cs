using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentUploadLifecycleTests
{
    [Fact]
    public async Task Mixed_batch_keeps_successful_file_and_returns_individual_failure()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, new DocumentUploadRequest
        {
            SharedMetadata = Metadata(),
            Files = new[]
            {
                Item("good.txt", "text/plain", "safe content"),
                Item("bad.zip", "application/zip", "not supported")
            }
        });

        Assert.True(result[0].Succeeded, result[0].Message);
        Assert.Equal(DocumentResultCode.UnsupportedFileType, result[1].Code);
        Assert.Equal(1, await harness.Context.Documents.CountAsync(d => d.IsAvailable));
        Assert.Equal(1, await harness.Context.DocumentActivities.CountAsync(a => a.ActionType == DocumentActivityType.Upload));
    }

    [Theory]
    [InlineData("CONTOSO_UNSAFE", DocumentResultCode.UnsafeFile)]
    [InlineData("CONTOSO_INDETERMINATE", DocumentResultCode.ScanIndeterminate)]
    public async Task Failed_scan_rejects_file_and_cleans_candidate(string marker, DocumentResultCode expectedCode)
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, Request(Item("rejected.txt", "text/plain", marker)));

        var upload = Assert.Single(result);
        Assert.False(upload.Succeeded);
        Assert.Equal(expectedCode, upload.Code);
        Assert.Empty(await harness.Context.Documents.ToListAsync());
        Assert.Empty(Directory.EnumerateFiles(harness.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Successful_upload_commits_opaque_file_and_activity()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, Request(Item("report.txt", "text/plain", "report content")));
        var upload = Assert.Single(result);
        var document = await harness.Context.Documents.SingleAsync();
        var activity = await harness.Context.DocumentActivities.SingleAsync();

        Assert.True(upload.Succeeded, upload.Message);
        Assert.True(document.IsAvailable);
        Assert.DoesNotContain("report.txt", document.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("documents/", document.FilePath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(harness.StorageRoot, document.FilePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(DocumentActivityType.Upload, activity.ActionType);
        Assert.Equal(document.Title, activity.DocumentTitleSnapshot);
    }

    [Fact]
    public async Task Storage_failure_does_not_create_available_row()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync(new ThrowingStorage());

        var result = await harness.Service.UploadAsync(4, Request(Item("failure.txt", "text/plain", "content")));

        var upload = Assert.Single(result);
        Assert.False(upload.Succeeded);
        Assert.Equal(DocumentResultCode.StorageFailure, upload.Code);
        Assert.Empty(await harness.Context.Documents.ToListAsync());
    }

    private static DocumentMetadataInput Metadata() => new()
    {
        Title = "Lifecycle test",
        Category = DocumentCategories.Other
    };

    private static DocumentUploadRequest Request(DocumentUploadItem item) => new()
    {
        SharedMetadata = Metadata(),
        Files = new[] { item }
    };

    private static DocumentUploadItem Item(string fileName, string contentType, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new DocumentUploadItem
        {
            OriginalFileName = fileName,
            ContentType = contentType,
            Length = bytes.LongLength,
            OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
        };
    }

    private sealed class DocumentServiceHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();
        private readonly FileStorageTestFixture _storage = new();

        private DocumentServiceHarness(IFileStorageService? storage)
        {
            Context = _database.CreateContext();
            Storage = storage ?? _storage.Storage;
            Service = new DocumentService(
                Context,
                Storage,
                new DeterministicLocalFileSafetyScanner(),
                new NotificationService(Context),
                NullLogger<DocumentService>.Instance);
        }

        public ApplicationDbContext Context { get; }
        public DocumentService Service { get; }
        public IFileStorageService Storage { get; }
        public string StorageRoot => _storage.RootPath;

        public static async Task<DocumentServiceHarness> CreateAsync(IFileStorageService? storage = null)
        {
            var harness = new DocumentServiceHarness(storage);
            await harness._database.EnsureCreatedAsync();
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _database.DisposeAsync();
            await _storage.DisposeAsync();
        }
    }

    private sealed class ThrowingStorage : IFileStorageService
    {
        public Task<StorageCandidate> StoreCandidateAsync(DocumentUploadItem upload, CancellationToken cancellationToken = default) =>
            throw new FileStorageException("test storage failure");

        public Task<string> CommitCandidateAsync(StorageCandidate candidate, string extension, CancellationToken cancellationToken = default) =>
            throw new FileStorageException("test storage failure");

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new FileStorageException("test storage failure");

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CleanupCandidateAsync(StorageCandidate candidate, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
