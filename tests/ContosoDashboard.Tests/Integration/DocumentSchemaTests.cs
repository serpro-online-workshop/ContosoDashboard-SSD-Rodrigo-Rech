using ContosoDashboard.Models;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Integration;

public sealed class DocumentSchemaTests
{
    [Fact]
    public async Task EnsureCreated_creates_document_tables_and_indexes()
    {
        await using var factory = new SqliteTestFactory();
        await factory.EnsureCreatedAsync();
        await using var context = factory.CreateContext();

        var tableNames = await context.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();

        Assert.Contains("Documents", tableNames);
        Assert.Contains("DocumentTags", tableNames);
        Assert.Contains("DocumentShares", tableNames);
        Assert.Contains("DocumentActivities", tableNames);

        var indexes = await context.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync();

        Assert.Contains(indexes, name => name.Contains("IX_Documents_UploaderId", StringComparison.Ordinal));
        Assert.Contains(indexes, name => name.Contains("IX_DocumentActivities_OccurredAtUtc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_share_target_is_rejected_by_database_constraint()
    {
        await using var factory = new SqliteTestFactory();
        await factory.EnsureCreatedAsync();
        await using var context = factory.CreateContext();

        context.Documents.Add(CreateDocument());
        await context.SaveChangesAsync();
        context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = 1,
            GrantedByUserId = 4,
            TargetUserId = 3,
            TargetDepartment = "Engineering"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_normalized_tag_is_rejected()
    {
        await using var factory = new SqliteTestFactory();
        await factory.EnsureCreatedAsync();
        await using var context = factory.CreateContext();

        context.Documents.Add(CreateDocument());
        await context.SaveChangesAsync();
        context.DocumentTags.AddRange(
            new DocumentTag { DocumentId = 1, TagText = "Finance", NormalizedTagText = "finance" },
            new DocumentTag { DocumentId = 1, TagText = "finance", NormalizedTagText = "finance" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_document_preserves_activity_snapshot()
    {
        await using var factory = new SqliteTestFactory();
        await factory.EnsureCreatedAsync();
        await using var context = factory.CreateContext();

        context.Documents.Add(CreateDocument());
        await context.SaveChangesAsync();
        context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = 1,
            ActorUserId = 4,
            ActionType = DocumentActivityType.Upload,
            DocumentTitleSnapshot = "Retained title",
            FileType = "text/plain"
        });
        await context.SaveChangesAsync();

        var document = await context.Documents.SingleAsync(d => d.DocumentId == 1);
        context.Documents.Remove(document);
        await context.SaveChangesAsync();

        var activity = await context.DocumentActivities.SingleAsync();
        Assert.Null(activity.DocumentId);
        Assert.Equal("Retained title", activity.DocumentTitleSnapshot);
    }

    [Fact]
    public async Task Document_model_exposes_required_lengths_and_keys()
    {
        await using var factory = new SqliteTestFactory();
        using var context = factory.CreateContext();
        var entity = context.Model.FindEntityType(typeof(Document));

        Assert.NotNull(entity);
        Assert.Equal(typeof(int), entity!.FindProperty(nameof(Document.DocumentId))!.ClrType);
        Assert.Equal(255, entity.FindProperty(nameof(Document.Title))!.GetMaxLength());
        Assert.Equal(255, entity.FindProperty(nameof(Document.FileType))!.GetMaxLength());
        Assert.Equal(typeof(long), entity.FindProperty(nameof(Document.FileSize))!.ClrType);
    }

    private static Document CreateDocument() => new()
    {
        Title = "Schema test document",
        Category = DocumentCategories.Other,
        UploaderId = 4,
        OriginalFileName = "schema.txt",
        FilePath = $"documents/{Guid.NewGuid():N}.txt",
        FileSize = 10,
        FileType = "text/plain",
        IsAvailable = true
    };
}
