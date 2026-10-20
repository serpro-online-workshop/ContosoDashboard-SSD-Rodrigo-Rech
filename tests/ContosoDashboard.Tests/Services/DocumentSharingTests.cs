using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentSharingTests
{
    [Fact]
    public async Task Owner_can_share_with_user_and_recipient_receives_notification()
    {
        await using var harness = await SharingHarness.CreateAsync();
        var documentId = await harness.UploadAsync();

        var result = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget { TargetUserId = 3 });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(DocumentResultCode.Success, result.Code);
        Assert.NotNull(await harness.Service.GetVisibleAsync(3, documentId));
        var notification = await harness.Context.Notifications.SingleAsync();
        Assert.Equal(3, notification.UserId);
        Assert.Equal(NotificationType.DocumentShared, notification.Type);
    }

    [Fact]
    public async Task Department_share_grants_access_without_project_membership()
    {
        await using var harness = await SharingHarness.CreateAsync();
        var documentId = await harness.UploadAsync();
        await harness.AddUserAsync(5, "hr.user@contoso.com", "Human Resources");

        var result = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget
        {
            TargetDepartment = "Human Resources"
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(await harness.Service.GetVisibleAsync(5, documentId));
        Assert.Single(await harness.Service.GetSharedWithMeAsync(5));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(3, "Engineering")]
    [InlineData(4, null)]
    public async Task Invalid_share_targets_do_not_grant_access(int? targetUserId, string? targetDepartment)
    {
        await using var harness = await SharingHarness.CreateAsync();
        var documentId = await harness.UploadAsync();

        var result = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget
        {
            TargetUserId = targetUserId,
            TargetDepartment = targetDepartment
        });

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentResultCode.InvalidTarget, result.Code);
        Assert.Empty(await harness.Context.DocumentShares.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_active_share_is_rejected_but_revoked_history_remains()
    {
        await using var harness = await SharingHarness.CreateAsync();
        var documentId = await harness.UploadAsync();
        var first = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget { TargetUserId = 3 });
        var duplicate = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget { TargetUserId = 3 });

        Assert.True(first.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.Equal(DocumentResultCode.InvalidTarget, duplicate.Code);

        var share = Assert.Single(await harness.Service.GetSharesAsync(4, documentId));
        var revoke = await harness.Service.RevokeShareAsync(4, share.DocumentShareId);

        Assert.True(revoke.Succeeded, revoke.Message);
        Assert.Null(await harness.Service.GetVisibleAsync(3, documentId));
        var history = Assert.Single(await harness.Context.DocumentShares.ToListAsync());
        Assert.NotNull(history.RevokedAtUtc);
    }

    [Fact]
    public async Task Notification_failure_does_not_commit_share()
    {
        await using var harness = await SharingHarness.CreateAsync(new ThrowingNotificationService());
        var documentId = await harness.UploadAsync();

        var result = await harness.Service.CreateShareAsync(4, documentId, new DocumentShareTarget { TargetUserId = 3 });

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentResultCode.PersistenceFailure, result.Code);
        Assert.Empty(await harness.Context.DocumentShares.ToListAsync());
        Assert.Null(await harness.Service.GetVisibleAsync(3, documentId));
    }

    private sealed class SharingHarness : IAsyncDisposable
    {
        private readonly SqliteTestFactory _database = new();
        private readonly FileStorageTestFixture _storage = new();

        private SharingHarness(INotificationService? notificationService)
        {
            Context = _database.CreateContext();
            Service = new DocumentService(
                Context,
                _storage.Storage,
                new DeterministicLocalFileSafetyScanner(),
                notificationService ?? new NotificationService(Context),
                NullLogger<DocumentService>.Instance);
        }

        public ApplicationDbContext Context { get; }
        public DocumentService Service { get; }

        public static async Task<SharingHarness> CreateAsync(INotificationService? notificationService = null)
        {
            var harness = new SharingHarness(notificationService);
            await harness._database.EnsureCreatedAsync();
            return harness;
        }

        public async Task<int> UploadAsync()
        {
            var bytes = "share content"u8.ToArray();
            var result = await Service.UploadAsync(4, new DocumentUploadRequest
            {
                SharedMetadata = new DocumentMetadataInput
                {
                    Title = "Shareable document",
                    Category = DocumentCategories.Other
                },
                Files = new[]
                {
                    new DocumentUploadItem
                    {
                        OriginalFileName = "share.txt",
                        ContentType = "text/plain",
                        Length = bytes.LongLength,
                        OpenReadAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                    }
                }
            });
            return Assert.Single(result).DocumentId!.Value;
        }

        public async Task AddUserAsync(int userId, string email, string department)
        {
            Context.Users.Add(new User
            {
                UserId = userId,
                Email = email,
                DisplayName = "Additional User",
                Department = department,
                Role = UserRole.Employee,
                AvailabilityStatus = AvailabilityStatus.Available
            });
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _database.DisposeAsync();
            await _storage.DisposeAsync();
        }
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false) => Task.FromResult(new List<Notification>());
        public Task<Notification> CreateNotificationAsync(Notification notification) => throw new InvalidOperationException("notification failure");
        public Task<bool> MarkAsReadAsync(int notificationId, int requestingUserId) => Task.FromResult(false);
        public Task<int> GetUnreadCountAsync(int userId) => Task.FromResult(0);
    }
}
