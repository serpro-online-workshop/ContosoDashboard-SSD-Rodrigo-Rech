using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentAuthorizationTests
{
    [Fact]
    public async Task Owner_can_read_personal_document()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        var documentId = await harness.AddDocumentAsync(uploaderId: 4);

        var document = await harness.Service.GetVisibleAsync(4, documentId);

        Assert.NotNull(document);
        Assert.Equal(documentId, document.DocumentId);
        Assert.True(document.CanEdit);
    }

    [Fact]
    public async Task Project_member_can_read_project_document()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        var documentId = await harness.AddDocumentAsync(uploaderId: 4, projectId: 1);

        var document = await harness.Service.GetVisibleAsync(3, documentId);

        Assert.NotNull(document);
        Assert.Equal("ContosoDashboard Development", document.ProjectName);
    }

    [Fact]
    public async Task Project_manager_and_administrator_can_read_project_document()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        var documentId = await harness.AddDocumentAsync(uploaderId: 4, projectId: 1);

        Assert.NotNull(await harness.Service.GetVisibleAsync(2, documentId));
        Assert.NotNull(await harness.Service.GetVisibleAsync(1, documentId));
    }

    [Fact]
    public async Task Team_lead_can_read_team_member_document()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        var documentId = await harness.AddDocumentAsync(uploaderId: 4);

        Assert.NotNull(await harness.Service.GetVisibleAsync(3, documentId));
    }

    [Fact]
    public async Task Unrelated_employee_gets_same_not_found_result_for_personal_document()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        var documentId = await harness.AddDocumentAsync(uploaderId: 3);

        Assert.Null(await harness.Service.GetVisibleAsync(4, documentId));
        Assert.Null(await harness.Service.GetVisibleAsync(4, 999999));
    }

    [Fact]
    public async Task List_excludes_documents_before_metadata_is_returned()
    {
        await using var harness = await AuthorizationHarness.CreateAsync();
        await harness.AddDocumentAsync(uploaderId: 4);
        await harness.AddDocumentAsync(uploaderId: 3);

        var page = await harness.Service.ListVisibleAsync(4, new DocumentQueryOptions());

        var document = Assert.Single(page.Items);
        Assert.Equal(4, document.UploaderId);
    }

    private sealed class AuthorizationHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();
        private readonly FileStorageTestFixture _storage = new();

        private AuthorizationHarness()
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

        public static async Task<AuthorizationHarness> CreateAsync()
        {
            var harness = new AuthorizationHarness();
            await harness._database.EnsureCreatedAsync();
            return harness;
        }

        public async Task<int> AddDocumentAsync(int uploaderId, int? projectId = null)
        {
            var document = new Document
            {
                Title = $"Document {uploaderId} {projectId}",
                Category = DocumentCategories.Other,
                UploaderId = uploaderId,
                ProjectId = projectId,
                OriginalFileName = "document.txt",
                FilePath = $"documents/{Guid.NewGuid():N}.txt",
                FileSize = 12,
                FileType = "text/plain",
                UploadedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                IsAvailable = true
            };
            Context.Documents.Add(document);
            await Context.SaveChangesAsync();
            return document.DocumentId;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _database.DisposeAsync();
            await _storage.DisposeAsync();
        }
    }
}
