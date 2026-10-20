using System.Diagnostics;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Integration;

public sealed class DocumentSearchPerformanceTests
{
    [Fact]
    public async Task Search_over_500_visible_documents_returns_authorized_results_within_two_seconds()
    {
        await using var database = new SqliteTestFactory();
        await using var storage = new FileStorageTestFixture();
        await database.EnsureCreatedAsync();
        await using var context = database.CreateContext();
        for (var index = 0; index < 500; index++)
        {
            context.Documents.Add(new Document
            {
                Title = $"Performance document {index}",
                Description = "representative search data",
                Category = DocumentCategories.Other,
                UploaderId = 4,
                OriginalFileName = $"performance-{index}.txt",
                FilePath = $"documents/{Guid.NewGuid():N}.txt",
                FileSize = index + 1,
                FileType = "text/plain",
                UploadedAtUtc = DateTime.UtcNow.AddMinutes(-index),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-index),
                IsAvailable = true
            });
        }
        await context.SaveChangesAsync();

        var service = new DocumentService(
            context,
            storage.Storage,
            new DeterministicLocalFileSafetyScanner(),
            new NotificationService(context),
            NullLogger<DocumentService>.Instance);
        var stopwatch = Stopwatch.StartNew();
        var page = await service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            SearchTerm = "representative",
            PageSize = 500
        });
        stopwatch.Stop();

        Assert.Equal(500, page.TotalCount);
        Assert.Equal(500, page.Items.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Search took {stopwatch.Elapsed}.");
    }
}
