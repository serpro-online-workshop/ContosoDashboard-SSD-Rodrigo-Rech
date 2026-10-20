using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentQueryTests
{
    [Fact]
    public async Task Search_matches_title_description_tag_uploader_and_project()
    {
        await using var harness = await QueryHarness.CreateAsync();
        await harness.AddDocumentAsync(4, "Roadmap", "delivery plan", new[] { "planning" });
        await harness.AddDocumentAsync(3, "Team note", "engineering update", new[] { "release" }, projectId: 1);

        Assert.Equal("Roadmap", (await harness.SearchAsync("Roadmap")).Single().Title);
        Assert.Equal("Roadmap", (await harness.SearchAsync("delivery")).Single().Title);
        Assert.Equal("Roadmap", (await harness.SearchAsync("planning")).Single().Title);
        Assert.Equal("Team note", (await harness.SearchAsync("Floris")).Single().Title);
        Assert.Equal("Team note", (await harness.SearchAsync("ContosoDashboard Development")).Single().Title);
    }

    [Fact]
    public async Task Filters_sorting_and_pagination_are_applied_to_visible_documents()
    {
        await using var harness = await QueryHarness.CreateAsync();
        await harness.AddDocumentAsync(4, "Zeta", category: DocumentCategories.Reports, size: 20, uploadedAtUtc: DateTime.UtcNow.AddDays(-1));
        await harness.AddDocumentAsync(4, "Alpha", category: DocumentCategories.Other, size: 10, uploadedAtUtc: DateTime.UtcNow.AddDays(-2));
        await harness.AddDocumentAsync(3, "Project report", category: DocumentCategories.Reports, size: 30, projectId: 1);

        var sorted = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            SortBy = DocumentSortField.Title,
            Descending = false,
            PageSize = 50
        });
        Assert.Equal(new[] { "Alpha", "Project report", "Zeta" }, sorted.Items.Select(item => item.Title));

        var filtered = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            Category = DocumentCategories.Reports,
            DateFromUtc = DateTime.UtcNow.AddDays(-3),
            DateToUtc = DateTime.UtcNow,
            PageSize = 50
        });
        Assert.Equal(2, filtered.TotalCount);

        var page = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            SortBy = DocumentSortField.FileSize,
            Descending = true,
            PageNumber = 2,
            PageSize = 1
        });
        Assert.Equal("Zeta", page.Items.Single().Title);
    }

    [Fact]
    public async Task Reversed_date_range_and_unknown_project_return_empty_page()
    {
        await using var harness = await QueryHarness.CreateAsync();
        await harness.AddDocumentAsync(4, "Visible");

        var reversed = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            DateFromUtc = DateTime.UtcNow,
            DateToUtc = DateTime.UtcNow.AddDays(-1)
        });
        var unknownProject = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions { ProjectId = 999 });

        Assert.Empty(reversed.Items);
        Assert.Empty(unknownProject.Items);
    }

    [Fact]
    public async Task Unauthorized_document_is_excluded_from_search_and_pagination()
    {
        await using var harness = await QueryHarness.CreateAsync();
        await harness.AddDocumentAsync(3, "Private secret");
        await harness.AddDocumentAsync(4, "Visible document");

        var page = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions
        {
            SearchTerm = "secret",
            PageSize = 50
        });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    private sealed class QueryHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();
        private readonly FileStorageTestFixture _storage = new();

        private QueryHarness()
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

        public static async Task<QueryHarness> CreateAsync()
        {
            var harness = new QueryHarness();
            await harness._database.EnsureCreatedAsync();
            return harness;
        }

        public async Task<int> AddDocumentAsync(
            int uploaderId,
            string title,
            string? description = null,
            IReadOnlyCollection<string>? tags = null,
            int? projectId = null,
            string category = DocumentCategories.Other,
            long size = 10,
            DateTime? uploadedAtUtc = null)
        {
            var document = new Document
            {
                Title = title,
                Description = description,
                Category = category,
                UploaderId = uploaderId,
                ProjectId = projectId,
                OriginalFileName = $"{title}.txt",
                FilePath = $"documents/{Guid.NewGuid():N}.txt",
                FileSize = size,
                FileType = "text/plain",
                UploadedAtUtc = uploadedAtUtc ?? DateTime.UtcNow,
                UpdatedAtUtc = uploadedAtUtc ?? DateTime.UtcNow,
                IsAvailable = true,
                Tags = (tags ?? Array.Empty<string>()).Select(tag => new DocumentTag
                {
                    TagText = tag,
                    NormalizedTagText = tag.ToUpperInvariant()
                }).ToList()
            };
            Context.Documents.Add(document);
            await Context.SaveChangesAsync();
            return document.DocumentId;
        }

        public async Task<IReadOnlyList<DocumentSummary>> SearchAsync(string searchTerm)
        {
            var page = await Service.ListVisibleAsync(4, new DocumentQueryOptions { SearchTerm = searchTerm });
            return page.Items;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _database.DisposeAsync();
            await _storage.DisposeAsync();
        }
    }
}
