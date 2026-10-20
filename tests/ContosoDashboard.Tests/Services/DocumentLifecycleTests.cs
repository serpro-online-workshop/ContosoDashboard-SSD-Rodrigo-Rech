using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentLifecycleTests
{
    [Fact]
    public async Task Owner_can_update_metadata_and_tags()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("old.txt", "old title");

        var result = await harness.Service.UpdateMetadataAsync(4, documentId, new DocumentMetadataInput
        {
            Title = " Updated title ",
            Description = "Updated description",
            Category = DocumentCategories.Reports,
            Tags = new[] { "Quarterly", "Finance" }
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Updated title", result.Document!.Title);
        Assert.Equal(DocumentCategories.Reports, result.Document.Category);
        Assert.Equal(new[] { "Quarterly", "Finance" }, result.Document.Tags);
    }

    [Fact]
    public async Task Unauthorized_metadata_update_leaves_document_unchanged()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("owned.txt", "Owner title");

        var result = await harness.Service.UpdateMetadataAsync(3, documentId, new DocumentMetadataInput
        {
            Title = "Changed",
            Category = DocumentCategories.Reports
        });

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentResultCode.NotFound, result.Code);
        Assert.Equal("Owner title", (await harness.Service.GetVisibleAsync(4, documentId))!.Title);
    }

    [Fact]
    public async Task Stale_metadata_update_returns_concurrency_conflict()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("stale.txt", "Original");
        var original = await harness.Context.Documents.SingleAsync(document => document.DocumentId == documentId);

        var result = await harness.Service.UpdateMetadataAsync(4, documentId, new DocumentMetadataInput
        {
            Title = "Changed",
            Category = DocumentCategories.Other
        }, original.UpdatedAtUtc.AddSeconds(-1));

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentResultCode.ConcurrencyConflict, result.Code);
    }

    [Fact]
    public async Task Safe_replacement_updates_current_file_and_records_activity()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("old.txt", "Original");
        var oldPath = (await harness.Context.Documents.SingleAsync(document => document.DocumentId == documentId)).FilePath;

        var result = await harness.Service.ReplaceFileAsync(4, documentId, Replacement("new.txt", "Replacement"));
        var document = await harness.Context.Documents.SingleAsync(item => item.DocumentId == documentId);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("new.txt", document.OriginalFileName);
        Assert.NotEqual(oldPath, document.FilePath);
        Assert.False(File.Exists(Path.Combine(harness.Storage.RootPath, oldPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(DocumentActivityType.Replace, await harness.Context.DocumentActivities
            .Where(activity => activity.DocumentId == documentId)
            .OrderByDescending(activity => activity.OccurredAtUtc)
            .Select(activity => activity.ActionType)
            .FirstAsync());
    }

    [Fact]
    public async Task Unsafe_replacement_preserves_previous_file_and_metadata()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("old.txt", "Original");
        var before = await harness.Context.Documents.AsNoTracking().SingleAsync(document => document.DocumentId == documentId);

        var result = await harness.Service.ReplaceFileAsync(4, documentId, Replacement("new.txt", "CONTOSO_UNSAFE"));
        var after = await harness.Context.Documents.AsNoTracking().SingleAsync(document => document.DocumentId == documentId);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentResultCode.UnsafeFile, result.Code);
        Assert.Equal(before.FilePath, after.FilePath);
        Assert.Equal(before.OriginalFileName, after.OriginalFileName);
        Assert.True(File.Exists(Path.Combine(harness.Storage.RootPath, before.FilePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(Directory.EnumerateFiles(harness.Storage.RootPath, "*", SearchOption.AllDirectories)
            .Where(path => path.Contains("candidates", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Delete_removes_document_and_retains_title_snapshot()
    {
        await using var harness = await LifecycleHarness.CreateAsync();
        var documentId = await harness.UploadAsync("delete.txt", "Keep this title");
        var before = await harness.Context.Documents.AsNoTracking().SingleAsync(document => document.DocumentId == documentId);

        var result = await harness.Service.DeleteAsync(4, documentId);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(await harness.Service.GetVisibleAsync(4, documentId));
        var activity = await harness.Context.DocumentActivities
            .Where(item => item.ActionType == DocumentActivityType.Delete)
            .SingleAsync();
        Assert.Null(activity.DocumentId);
        Assert.Equal(before.Title, activity.DocumentTitleSnapshot);
        Assert.False(File.Exists(Path.Combine(harness.Storage.RootPath, before.FilePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static DocumentReplacementRequest Replacement(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new DocumentReplacementRequest
        {
            OriginalFileName = fileName,
            ContentType = "text/plain",
            Length = bytes.LongLength,
            OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
        };
    }

    private sealed class LifecycleHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();

        private LifecycleHarness(FileStorageTestFixture storage)
        {
            Storage = storage;
            Context = _database.CreateContext();
            Service = new DocumentService(
                Context,
                Storage.Storage,
                new DeterministicLocalFileSafetyScanner(),
                new NotificationService(Context),
                NullLogger<DocumentService>.Instance);
        }

        public FileStorageTestFixture Storage { get; }
        public ApplicationDbContext Context { get; }
        public DocumentService Service { get; }

        public static async Task<LifecycleHarness> CreateAsync()
        {
            var harness = new LifecycleHarness(new FileStorageTestFixture());
            await harness._database.EnsureCreatedAsync();
            return harness;
        }

        public async Task<int> UploadAsync(string fileName, string title)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("Original file");
            var result = await Service.UploadAsync(4, new DocumentUploadRequest
            {
                SharedMetadata = new DocumentMetadataInput { Title = title, Category = DocumentCategories.Other },
                Files = new[]
                {
                    new DocumentUploadItem
                    {
                        OriginalFileName = fileName,
                        ContentType = "text/plain",
                        Length = bytes.LongLength,
                        OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                    }
                }
            });
            return Assert.Single(result).DocumentId!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _database.DisposeAsync();
            await Storage.DisposeAsync();
        }
    }
}
