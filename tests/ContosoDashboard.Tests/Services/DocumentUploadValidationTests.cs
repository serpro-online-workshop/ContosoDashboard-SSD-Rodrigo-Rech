using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentUploadValidationTests
{
    [Theory]
    [InlineData(DocumentCategories.ProjectDocuments)]
    [InlineData(DocumentCategories.TeamResources)]
    [InlineData(DocumentCategories.PersonalFiles)]
    [InlineData(DocumentCategories.Reports)]
    [InlineData(DocumentCategories.Presentations)]
    [InlineData(DocumentCategories.Other)]
    public async Task Upload_accepts_each_named_category_after_trimming(string category)
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, Request(
            new DocumentMetadataInput { Title = "  Quarterly plan  ", Category = $" {category} " },
            Item("plan.txt", "text/plain", "plan")));

        var upload = Assert.Single(result);
        Assert.True(upload.Succeeded, upload.Message);
        Assert.Equal("Quarterly plan", upload.Document!.Title);
        Assert.Equal(category, upload.Document.Category);
    }

    [Theory]
    [InlineData("file.pdf", "application/pdf")]
    [InlineData("file.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("file.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("file.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("file.txt", "text/plain")]
    [InlineData("file.jpeg", "image/jpeg")]
    [InlineData("file.png", "image/png")]
    public async Task Upload_accepts_supported_extension_and_content_type(string fileName, string contentType)
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, Request(
            new DocumentMetadataInput { Title = "Supported file", Category = DocumentCategories.Other },
            Item(fileName, contentType, "safe content")));

        Assert.True(Assert.Single(result).Succeeded);
    }

    [Fact]
    public async Task Upload_rejects_missing_required_trimmed_metadata()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, Request(
            new DocumentMetadataInput { Title = " ", Category = "" },
            Item("missing.txt", "text/plain", "safe content")));

        var upload = Assert.Single(result);
        Assert.False(upload.Succeeded);
        Assert.Equal(DocumentResultCode.MissingRequiredMetadata, upload.Code);
        Assert.Empty(harness.Context.Documents);
    }

    [Fact]
    public async Task Upload_rejects_mismatched_and_unsupported_types()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, new DocumentUploadRequest
        {
            SharedMetadata = new DocumentMetadataInput { Title = "Type test", Category = DocumentCategories.Other },
            Files = new[]
            {
                Item("wrong.pdf", "text/plain", "safe content"),
                Item("archive.zip", "application/zip", "safe content")
            }
        });

        Assert.All(result, upload =>
        {
            Assert.False(upload.Succeeded);
            Assert.Equal(DocumentResultCode.UnsupportedFileType, upload.Code);
        });
        Assert.Empty(harness.Context.Documents);
    }

    [Fact]
    public async Task Upload_accepts_exact_limit_and_rejects_one_byte_over()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();
        var exactLimit = new byte[DocumentStorageLimits.MaxFileSizeBytes];

        var accepted = await harness.Service.UploadAsync(4, Request(
            new DocumentMetadataInput { Title = "Limit", Category = DocumentCategories.Other },
            Item("limit.txt", "text/plain", exactLimit)));
        var rejected = await harness.Service.UploadAsync(4, Request(
            new DocumentMetadataInput { Title = "Too large", Category = DocumentCategories.Other },
            new DocumentUploadItem
            {
                OriginalFileName = "too-large.txt",
                ContentType = "text/plain",
                Length = DocumentStorageLimits.MaxFileSizeBytes + 1,
                OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 }))
            }));

        Assert.True(Assert.Single(accepted).Succeeded);
        Assert.Equal(DocumentResultCode.FileTooLarge, Assert.Single(rejected).Code);
    }

    [Fact]
    public async Task Upload_applies_per_file_metadata_override_independently()
    {
        await using var harness = await DocumentServiceHarness.CreateAsync();

        var result = await harness.Service.UploadAsync(4, new DocumentUploadRequest
        {
            SharedMetadata = new DocumentMetadataInput
            {
                Title = "Shared title",
                Category = DocumentCategories.Other,
                Tags = new[] { "Shared" }
            },
            Files = new[]
            {
                Item("first.txt", "text/plain", "first"),
                new DocumentUploadItem
                {
                    OriginalFileName = "second.txt",
                    ContentType = "text/plain",
                    Length = 6,
                    MetadataOverride = new DocumentMetadataInput
                    {
                        Title = "Second title",
                        Category = DocumentCategories.Reports,
                        Tags = new[] { "Quarterly" }
                    },
                    OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream("second"u8.ToArray()))
                }
            }
        });

        Assert.All(result, upload => Assert.True(upload.Succeeded, upload.Message));
        Assert.Equal(new[] { "Shared title", "Second title" }, result.Select(upload => upload.Document!.Title));
        Assert.Equal(new[] { DocumentCategories.Other, DocumentCategories.Reports }, result.Select(upload => upload.Document!.Category));
    }

    private static DocumentUploadRequest Request(DocumentMetadataInput metadata, DocumentUploadItem item) => new()
    {
        SharedMetadata = metadata,
        Files = new[] { item }
    };

    private static DocumentUploadItem Item(string fileName, string contentType, string content) => Item(fileName, contentType, System.Text.Encoding.UTF8.GetBytes(content));

    private static DocumentUploadItem Item(string fileName, string contentType, byte[] content) => new()
    {
        OriginalFileName = fileName,
        ContentType = contentType,
        Length = content.LongLength,
        OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(content, writable: false))
    };

    private sealed class DocumentServiceHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();
        private readonly FileStorageTestFixture _storage = new();

        private DocumentServiceHarness()
        {
            Context = _database.CreateContext();
            Service = new DocumentService(
                Context,
                _storage.Storage,
                new DeterministicLocalFileSafetyScanner(),
                new NotificationService(Context),
                NullLogger<DocumentService>.Instance);
        }

        public ApplicationDbContext Context { get; }
        public DocumentService Service { get; }

        public static async Task<DocumentServiceHarness> CreateAsync()
        {
            var harness = new DocumentServiceHarness();
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
}
