using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Integration;

public sealed class DocumentFileEndpointTests
{
    [Fact]
    public async Task Authorized_preview_and_download_use_safe_filename_and_content_type()
    {
        await using var database = new SqliteTestFactory();
        await using var storage = new FileStorageTestFixture();
        await database.EnsureCreatedAsync();
        await using var context = database.CreateContext();
        var bytes = "%PDF-1.4 safe"u8.ToArray();
        var candidate = await storage.Storage.StoreCandidateAsync(new DocumentUploadItem
        {
            OriginalFileName = "source.pdf",
            ContentType = "application/pdf",
            Length = bytes.LongLength,
            OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
        });
        var path = await storage.Storage.CommitCandidateAsync(candidate, ".pdf");
        var document = new Document
        {
            Title = "PDF document",
            Category = DocumentCategories.Other,
            UploaderId = 4,
            OriginalFileName = "../../quarterly report.pdf",
            FilePath = path,
            FileSize = bytes.LongLength,
            FileType = "application/pdf",
            IsAvailable = true
        };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        var service = new DocumentService(
            context,
            storage.Storage,
            new DeterministicLocalFileSafetyScanner(),
            new NotificationService(context),
            NullLogger<DocumentService>.Instance);

        await using var access = await service.OpenFileAsync(4, document.DocumentId, preview: true);

        Assert.NotNull(access);
        Assert.Equal("quarterly report.pdf", access.FileName);
        Assert.Equal("application/pdf", access.ContentType);
        Assert.True(access.IsPreview);
        Assert.Equal(DocumentActivityType.Preview, context.DocumentActivities.Single().ActionType);
    }

    [Fact]
    public async Task Non_previewable_file_is_not_opened_as_preview()
    {
        await using var database = new SqliteTestFactory();
        await using var storage = new FileStorageTestFixture();
        await database.EnsureCreatedAsync();
        await using var context = database.CreateContext();
        context.Documents.Add(new Document
        {
            Title = "Text document",
            Category = DocumentCategories.Other,
            UploaderId = 4,
            OriginalFileName = "text.txt",
            FilePath = "documents/text.txt",
            FileSize = 4,
            FileType = "text/plain",
            IsAvailable = true
        });
        await context.SaveChangesAsync();
        var service = new DocumentService(
            context,
            storage.Storage,
            new DeterministicLocalFileSafetyScanner(),
            new NotificationService(context),
            NullLogger<DocumentService>.Instance);

        Assert.Null(await service.OpenFileAsync(4, 1, preview: true));
    }

    [Fact]
    public async Task File_access_is_not_returned_for_unknown_or_unauthorized_documents()
    {
        await using var database = new SqliteTestFactory();
        await using var storage = new FileStorageTestFixture();
        await database.EnsureCreatedAsync();
        await using var context = database.CreateContext();
        context.Documents.Add(new Document
        {
            Title = "Private file",
            Category = DocumentCategories.Other,
            UploaderId = 3,
            OriginalFileName = "private.txt",
            FilePath = "documents/private.txt",
            FileSize = 4,
            FileType = "text/plain",
            IsAvailable = true
        });
        await context.SaveChangesAsync();
        var service = new DocumentService(
            context,
            storage.Storage,
            new DeterministicLocalFileSafetyScanner(),
            new NotificationService(context),
            NullLogger<DocumentService>.Instance);

        Assert.Null(await service.OpenFileAsync(4, 1, preview: false));
        Assert.Null(await service.OpenFileAsync(4, 999, preview: false));
    }
}
