using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileSafetyScanner _safetyScanner;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IFileSafetyScanner safetyScanner,
        INotificationService notificationService,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _safetyScanner = safetyScanner;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<DocumentPage> ListVisibleAsync(int actorUserId, DocumentQueryOptions options, CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return new DocumentPage { PageNumber = 1, PageSize = 0 };
        }

        options ??= new DocumentQueryOptions();
        var pageNumber = Math.Max(1, options.PageNumber);
        var pageSize = Math.Clamp(options.PageSize <= 0 ? 50 : options.PageSize, 1, 500);
        var candidates = await _context.Documents
            .AsNoTracking()
            .Where(document => document.IsAvailable)
            .Include(document => document.Uploader)
            .Include(document => document.Project)
            .ThenInclude(project => project.ProjectMembers)
            .Include(document => document.Tags)
            .Include(document => document.Shares)
            .ToListAsync(cancellationToken);

        var visible = candidates
            .Where(document => CanAccess(actor, document))
            .Where(document => Matches(document, options))
            .Take(500)
            .ToList();

        var ordered = ApplySort(visible, options.SortBy, options.Descending);
        var items = new List<DocumentSummary>();
        foreach (var document in ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize))
        {
            items.Add(ToSummary(document, actor));
        }

        return new DocumentPage
        {
            Items = items,
            TotalCount = ordered.Count(),
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<DocumentSummary?> GetVisibleAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return null;
        }

        var document = await _context.Documents
            .AsNoTracking()
            .Where(item => item.DocumentId == documentId && item.IsAvailable)
            .Include(item => item.Uploader)
            .Include(item => item.Project)
            .ThenInclude(project => project.ProjectMembers)
            .Include(item => item.Tags)
            .Include(item => item.Shares)
            .SingleOrDefaultAsync(cancellationToken);
        return document != null && CanAccess(actor, document)
            ? ToSummary(document, actor)
            : null;
    }

    public async Task<IReadOnlyList<DocumentUploadResult>> UploadAsync(
        int actorUserId,
        DocumentUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DocumentUploadResult>();
        foreach (var file in request.Files ?? Array.Empty<DocumentUploadItem>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await UploadOneAsync(actorUserId, request.SharedMetadata, file, cancellationToken));
        }

        return results;
    }

    public async Task<DocumentMutationResult> UpdateMetadataAsync(
        int actorUserId,
        int documentId,
        DocumentMetadataInput metadata,
        DateTime? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (actor, document) = await LoadMutationDocumentAsync(actorUserId, documentId, cancellationToken);
        if (actor == null || document == null || !CanManage(actor, document))
        {
            return MutationFailure(DocumentResultCode.NotFound, "The document was not found.");
        }

        if (expectedUpdatedAtUtc.HasValue && document.UpdatedAtUtc != expectedUpdatedAtUtc.Value)
        {
            return MutationFailure(DocumentResultCode.ConcurrencyConflict, "The document changed before this update was submitted.");
        }

        var title = metadata.Title ?? document.Title;
        var description = metadata.Description ?? document.Description;
        var category = metadata.Category ?? document.Category;
        var projectId = metadata.ProjectId ?? document.ProjectId;
        var taskId = metadata.TaskId ?? document.TaskId;
        var validationCode = ValidateMetadataValues(title, description, category, metadata.Tags);
        if (validationCode.HasValue)
        {
            return MutationFailure(validationCode.Value, ValidationMessage(validationCode.Value));
        }

        var associationCode = await ValidateAssociationAsync(actorUserId, projectId, taskId, cancellationToken);
        if (associationCode.HasValue)
        {
            return MutationFailure(associationCode.Value, ValidationMessage(associationCode.Value));
        }

        document.Title = title!.Trim();
        document.Description = NormalizeOptional(description);
        document.Category = category!.Trim();
        document.ProjectId = projectId;
        document.TaskId = taskId;
        document.UpdatedAtUtc = DateTime.UtcNow;
        if (metadata.Tags != null)
        {
            _context.DocumentTags.RemoveRange(document.Tags);
            document.Tags = NormalizeTags(metadata.Tags)
                .Select(tag => new DocumentTag
                {
                    DocumentId = document.DocumentId,
                    TagText = tag,
                    NormalizedTagText = tag.ToUpperInvariant(),
                    CreatedAtUtc = DateTime.UtcNow
                })
                .ToList();
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new DocumentMutationResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document metadata updated.",
                Document = await ToSummaryAsync(document, actorUserId, cancellationToken)
            };
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not update document metadata for {DocumentId}.", documentId);
            _context.ChangeTracker.Clear();
            return MutationFailure(DocumentResultCode.PersistenceFailure, "The document metadata could not be saved.");
        }
    }

    public async Task<DocumentMutationResult> ReplaceFileAsync(
        int actorUserId,
        int documentId,
        DocumentReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        var (actor, document) = await LoadMutationDocumentAsync(actorUserId, documentId, cancellationToken);
        if (actor == null || document == null || !CanManage(actor, document))
        {
            return MutationFailure(DocumentResultCode.NotFound, "The document was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.OriginalFileName)
            || request.OriginalFileName.Trim().Length > 255
            || request.Length <= 0)
        {
            return MutationFailure(DocumentResultCode.MissingRequiredMetadata, "The replacement file is invalid.");
        }

        if (request.Length > DocumentStorageLimits.MaxFileSizeBytes)
        {
            return MutationFailure(DocumentResultCode.FileTooLarge, "The file exceeds the 25 MiB limit.");
        }

        if (!DocumentFileTypes.IsSupported(request.OriginalFileName, request.ContentType))
        {
            return MutationFailure(DocumentResultCode.UnsupportedFileType, "The replacement file type is not supported.");
        }

        StorageCandidate? candidate = null;
        string? newPath = null;
        var oldPath = document.FilePath;
        try
        {
            candidate = await _fileStorage.StoreCandidateAsync(new DocumentUploadItem
            {
                OriginalFileName = request.OriginalFileName,
                ContentType = request.ContentType,
                Length = request.Length,
                OpenReadAsync = request.OpenReadAsync
            }, cancellationToken);
            await using (var candidateContent = await _fileStorage.OpenReadAsync(candidate.RelativePath, cancellationToken))
            {
                var scan = await _safetyScanner.ScanAsync(
                    candidateContent,
                    request.OriginalFileName,
                    request.ContentType,
                    cancellationToken);
                if (scan.Status != FileSafetyStatus.Safe)
                {
                    await _fileStorage.CleanupCandidateAsync(candidate, cancellationToken);
                    return MutationFailure(
                        scan.Status == FileSafetyStatus.Unsafe
                            ? DocumentResultCode.UnsafeFile
                            : DocumentResultCode.ScanIndeterminate,
                        scan.Message);
                }
            }

            newPath = await _fileStorage.CommitCandidateAsync(
                candidate,
                DocumentFileTypes.GetNormalizedExtension(request.OriginalFileName),
                cancellationToken);
            document.OriginalFileName = Path.GetFileName(request.OriginalFileName.Trim());
            document.FilePath = newPath;
            document.FileSize = candidate.Length;
            document.FileType = request.ContentType.Trim();
            document.UpdatedAtUtc = DateTime.UtcNow;
            document.Activities.Add(new DocumentActivity
            {
                ActorUserId = actorUserId,
                ActionType = DocumentActivityType.Replace,
                DocumentTitleSnapshot = document.Title,
                UploaderDisplayNameSnapshot = document.Uploader.DisplayName,
                FileType = document.FileType,
                OccurredAtUtc = DateTime.UtcNow,
                Details = "Document file replaced."
            });
            await _context.SaveChangesAsync(cancellationToken);
            await _fileStorage.DeleteAsync(oldPath, cancellationToken);

            return new DocumentMutationResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document file replaced.",
                Document = await ToSummaryAsync(document, actorUserId, cancellationToken)
            };
        }
        catch (FileTooLargeException exception)
        {
            if (candidate != null) await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            return MutationFailure(DocumentResultCode.FileTooLarge, exception.Message);
        }
        catch (FileStorageException exception)
        {
            if (candidate != null) await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            if (newPath != null) await _fileStorage.DeleteAsync(newPath, CancellationToken.None);
            return MutationFailure(DocumentResultCode.StorageFailure, "The replacement file could not be stored.");
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not persist replacement for document {DocumentId}.", documentId);
            _context.ChangeTracker.Clear();
            if (candidate != null) await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            if (newPath != null) await _fileStorage.DeleteAsync(newPath, CancellationToken.None);
            return MutationFailure(DocumentResultCode.PersistenceFailure, "The replacement could not be saved.");
        }
        catch (OperationCanceledException)
        {
            if (candidate != null) await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            if (newPath != null) await _fileStorage.DeleteAsync(newPath, CancellationToken.None);
            throw;
        }
    }

    public async Task<DocumentMutationResult> DeleteAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var (actor, document) = await LoadMutationDocumentAsync(actorUserId, documentId, cancellationToken);
        if (actor == null || document == null || !CanManage(actor, document))
        {
            return MutationFailure(DocumentResultCode.NotFound, "The document was not found.");
        }

        var oldPath = document.FilePath;
        var activity = new DocumentActivity
        {
            DocumentId = document.DocumentId,
            ActorUserId = actorUserId,
            ActionType = DocumentActivityType.Delete,
            DocumentTitleSnapshot = document.Title,
            UploaderDisplayNameSnapshot = document.Uploader.DisplayName,
            FileType = document.FileType,
            OccurredAtUtc = DateTime.UtcNow,
            Details = "Document permanently deleted."
        };
        _context.DocumentActivities.Add(activity);
        _context.Documents.Remove(document);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _fileStorage.DeleteAsync(oldPath, cancellationToken);
            return new DocumentMutationResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document permanently deleted."
            };
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not delete document {DocumentId}.", documentId);
            _context.ChangeTracker.Clear();
            return MutationFailure(DocumentResultCode.PersistenceFailure, "The document could not be deleted.");
        }
    }
    public async Task<IReadOnlyList<DocumentShareView>> GetSharesAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var (actor, document) = await LoadMutationDocumentAsync(actorUserId, documentId, cancellationToken);
        if (actor == null || document == null || !CanShare(actor, document))
        {
            return Array.Empty<DocumentShareView>();
        }

        return await _context.DocumentShares
            .AsNoTracking()
            .Where(share => share.DocumentId == documentId)
            .Include(share => share.TargetUser)
            .OrderByDescending(share => share.GrantedAtUtc)
            .Select(share => new DocumentShareView
            {
                DocumentShareId = share.DocumentShareId,
                DocumentId = share.DocumentId,
                TargetUserId = share.TargetUserId,
                TargetUserName = share.TargetUser == null ? null : share.TargetUser.DisplayName,
                TargetDepartment = share.TargetDepartment,
                GrantedByUserId = share.GrantedByUserId,
                GrantedAtUtc = share.GrantedAtUtc,
                RevokedAtUtc = share.RevokedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentMutationResult> CreateShareAsync(
        int actorUserId,
        int documentId,
        DocumentShareTarget target,
        CancellationToken cancellationToken = default)
    {
        var (actor, document) = await LoadMutationDocumentAsync(actorUserId, documentId, cancellationToken);
        if (actor == null || document == null || !CanShare(actor, document))
        {
            return MutationFailure(DocumentResultCode.NotFound, "The document was not found.");
        }

        var hasUserTarget = target.TargetUserId.HasValue;
        var department = NormalizeOptional(target.TargetDepartment);
        var hasDepartmentTarget = !string.IsNullOrWhiteSpace(department);
        if (hasUserTarget == hasDepartmentTarget)
        {
            return MutationFailure(DocumentResultCode.InvalidTarget, "Choose exactly one user or department target.");
        }

        List<int> recipientIds;
        if (hasUserTarget)
        {
            if (target.TargetUserId == actorUserId
                || !await _context.Users.AnyAsync(user => user.UserId == target.TargetUserId, cancellationToken))
            {
                return MutationFailure(DocumentResultCode.InvalidTarget, "The selected user is not a valid share target.");
            }

            if (await _context.DocumentShares.AnyAsync(
                share => share.DocumentId == documentId
                    && share.RevokedAtUtc == null
                    && share.TargetUserId == target.TargetUserId,
                cancellationToken))
            {
                return MutationFailure(DocumentResultCode.InvalidTarget, "An active share already exists for this user.");
            }

            recipientIds = new List<int> { target.TargetUserId.Value };
        }
        else
        {
            if (department!.Length > 100)
            {
                return MutationFailure(DocumentResultCode.InvalidTarget, "The department share target is invalid.");
            }

            recipientIds = await _context.Users
                .Where(user => user.Department != null && user.Department.ToLower() == department.ToLower() && user.UserId != actorUserId)
                .Select(user => user.UserId)
                .ToListAsync(cancellationToken);
            if (recipientIds.Count == 0)
            {
                return MutationFailure(DocumentResultCode.InvalidTarget, "The selected department has no available recipients.");
            }

            if (await _context.DocumentShares.AnyAsync(
                share => share.DocumentId == documentId
                    && share.RevokedAtUtc == null
                    && share.TargetDepartment != null
                    && share.TargetDepartment.ToLower() == department.ToLower(),
                cancellationToken))
            {
                return MutationFailure(DocumentResultCode.InvalidTarget, "An active share already exists for this department.");
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            _context.DocumentShares.Add(new DocumentShare
            {
                DocumentId = documentId,
                TargetUserId = target.TargetUserId,
                TargetDepartment = department,
                GrantedByUserId = actorUserId,
                GrantedAtUtc = now
            });
            _context.DocumentActivities.Add(new DocumentActivity
            {
                DocumentId = documentId,
                ActorUserId = actorUserId,
                ActionType = DocumentActivityType.Share,
                DocumentTitleSnapshot = document.Title,
                UploaderDisplayNameSnapshot = document.Uploader.DisplayName,
                FileType = document.FileType,
                OccurredAtUtc = now,
                Details = hasUserTarget ? "Document shared with a user." : "Document shared with a department."
            });
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var recipientId in recipientIds)
            {
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = recipientId,
                    Title = "A document was shared with you",
                    Message = $"{document.Title} is now available in Shared with Me.",
                    Type = NotificationType.DocumentShared,
                    Priority = NotificationPriority.Informational
                });
            }

            await transaction.CommitAsync(cancellationToken);
            return new DocumentMutationResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document share created.",
                Document = await ToSummaryAsync(document, actorUserId, cancellationToken)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not create share for document {DocumentId}.", documentId);
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            return MutationFailure(DocumentResultCode.PersistenceFailure, "The document share could not be created.");
        }
    }

    public async Task<DocumentMutationResult> RevokeShareAsync(
        int actorUserId,
        int documentShareId,
        CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        var share = await _context.DocumentShares
            .Include(item => item.Document)
                .ThenInclude(item => item.Uploader)
            .Include(item => item.Document)
                .ThenInclude(item => item.Project)
            .SingleOrDefaultAsync(item => item.DocumentShareId == documentShareId, cancellationToken);
        if (actor == null || share == null || !CanShare(actor, share.Document) || !share.IsActive)
        {
            return MutationFailure(DocumentResultCode.NotFound, "The document share was not found.");
        }

        share.RevokedAtUtc = DateTime.UtcNow;
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = share.DocumentId,
            ActorUserId = actorUserId,
            ActionType = DocumentActivityType.Share,
            DocumentTitleSnapshot = share.Document.Title,
            UploaderDisplayNameSnapshot = share.Document.Uploader.DisplayName,
            FileType = share.Document.FileType,
            OccurredAtUtc = DateTime.UtcNow,
            Details = "Document share revoked."
        });
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new DocumentMutationResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document share revoked."
            };
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not revoke document share {DocumentShareId}.", documentShareId);
            _context.ChangeTracker.Clear();
            return MutationFailure(DocumentResultCode.PersistenceFailure, "The document share could not be revoked.");
        }
    }
    public async Task<DocumentFileAccess?> OpenFileAsync(
        int actorUserId,
        int documentId,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return null;
        }

        var document = await _context.Documents
            .Where(item => item.DocumentId == documentId && item.IsAvailable)
            .Include(item => item.Uploader)
            .Include(item => item.Project)
                .ThenInclude(project => project.ProjectMembers)
            .Include(item => item.Tags)
            .Include(item => item.Shares)
            .SingleOrDefaultAsync(cancellationToken);
        if (document == null || !CanAccess(actor, document)
            || (preview && !DocumentFileTypes.IsPreviewable(document.FileType)))
        {
            return null;
        }

        Stream content;
        try
        {
            content = await _fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileStorageException exception)
        {
            _logger.LogWarning(exception, "Could not open document {DocumentId} for user {UserId}.", documentId, actorUserId);
            return null;
        }

        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId,
            ActorUserId = actorUserId,
            ActionType = preview ? DocumentActivityType.Preview : DocumentActivityType.Download,
            DocumentTitleSnapshot = document.Title,
            UploaderDisplayNameSnapshot = document.Uploader.DisplayName,
            FileType = document.FileType,
            OccurredAtUtc = DateTime.UtcNow,
            Details = preview ? "Document preview opened." : "Document downloaded."
        });
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not record document access for {DocumentId}.", documentId);
        }

        return new DocumentFileAccess
        {
            Content = content,
            FileName = SanitizeFileName(document.OriginalFileName, document.DocumentId),
            ContentType = document.FileType,
            IsPreview = preview
        };
    }
    public async Task<DocumentActivityReport> GetActivityReportAsync(
        int actorUserId,
        DocumentActivityReportOptions options,
        CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        var now = DateTime.UtcNow;
        var fromUtc = NormalizeUtc(options.FromUtc);
        var toUtc = NormalizeUtc(options.ToUtc);
        var empty = new DocumentActivityReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        if (actor?.Role != UserRole.Administrator
            || fromUtc > toUtc
            || fromUtc < now.AddMonths(-12)
            || toUtc > now)
        {
            return empty;
        }

        var activities = await _context.DocumentActivities
            .AsNoTracking()
            .Include(activity => activity.Document)
                .ThenInclude(document => document!.Uploader)
            .Where(activity => activity.OccurredAtUtc >= fromUtc && activity.OccurredAtUtc <= toUtc)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.ActionType)
            && Enum.TryParse<DocumentActivityType>(options.ActionType, true, out var actionType))
        {
            activities = activities.Where(activity => activity.ActionType == actionType).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(options.ActionType))
        {
            return empty;
        }

        if (!string.IsNullOrWhiteSpace(options.FileType))
        {
            activities = activities
                .Where(activity => string.Equals(activity.FileType, options.FileType.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (options.UploaderId.HasValue)
        {
            activities = activities
                .Where(activity => activity.Document?.UploaderId == options.UploaderId.Value)
                .ToList();
        }

        var rows = activities
            .GroupBy(activity => new
            {
                Action = activity.ActionType.ToString(),
                FileType = activity.FileType ?? string.Empty,
                Uploader = activity.Document?.Uploader?.DisplayName
                    ?? activity.UploaderDisplayNameSnapshot
                    ?? "Deleted document"
            })
            .Select(group => new DocumentActivityReportRow
            {
                ActionType = group.Key.Action,
                FileType = group.Key.FileType,
                Uploader = group.Key.Uploader,
                Count = group.Count()
            })
            .OrderBy(row => row.ActionType)
            .ThenBy(row => row.Uploader)
            .ThenBy(row => row.FileType)
            .ToList();

        return new DocumentActivityReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TotalActivities = activities.Count,
            Rows = rows
        };
    }

    public async Task<int> GetDocumentCountAsync(
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var (actor, documents) = await LoadAvailableDocumentsAsync(actorUserId, cancellationToken);
        return actor == null ? 0 : documents.Count(document => CanAccess(actor, document));
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetRecentUploadsAsync(
        int actorUserId,
        int take = 5,
        CancellationToken cancellationToken = default)
    {
        var (actor, documents) = await LoadAvailableDocumentsAsync(actorUserId, cancellationToken);
        if (actor == null)
        {
            return Array.Empty<DocumentSummary>();
        }

        return documents
            .Where(document => document.UploaderId == actorUserId && CanAccess(actor, document))
            .OrderByDescending(document => document.UploadedAtUtc)
            .ThenByDescending(document => document.DocumentId)
            .Take(Math.Clamp(take, 1, 50))
            .Select(document => ToSummary(document, actor))
            .ToList();
    }
    public async Task<IReadOnlyList<DocumentSummary>> GetSharedWithMeAsync(
        int actorUserId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return Array.Empty<DocumentSummary>();
        }

        var documents = await _context.Documents
            .AsNoTracking()
            .Where(document => document.IsAvailable)
            .Include(document => document.Uploader)
            .Include(document => document.Project)
                .ThenInclude(project => project.ProjectMembers)
            .Include(document => document.Tags)
            .Include(document => document.Shares)
            .OrderByDescending(document => document.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        return documents
            .Where(document => CanAccess(actor, document) && HasExplicitShare(actor, document))
            .Take(Math.Clamp(take, 1, 500))
            .Select(document => ToSummary(document, actor))
            .ToList();
    }

    private async Task<DocumentUploadResult> UploadOneAsync(
        int actorUserId,
        DocumentMetadataInput sharedMetadata,
        DocumentUploadItem file,
        CancellationToken cancellationToken)
    {
        var result = new DocumentUploadResult
        {
            OriginalFileName = file.OriginalFileName
        };
        var metadata = MergeMetadata(sharedMetadata, file.MetadataOverride);

        var validationCode = await ValidateUploadAsync(actorUserId, metadata, file, cancellationToken);
        if (validationCode.HasValue)
        {
            return Failure(result, validationCode.Value, ValidationMessage(validationCode.Value));
        }

        StorageCandidate? candidate = null;
        string? committedPath = null;
        try
        {
            candidate = await _fileStorage.StoreCandidateAsync(file, cancellationToken);
            await using (var candidateContent = await _fileStorage.OpenReadAsync(candidate.RelativePath, cancellationToken))
            {
                var scan = await _safetyScanner.ScanAsync(
                    candidateContent,
                    file.OriginalFileName,
                    file.ContentType,
                    cancellationToken);
                if (scan.Status != FileSafetyStatus.Safe)
                {
                    await _fileStorage.CleanupCandidateAsync(candidate, cancellationToken);
                    var code = scan.Status == FileSafetyStatus.Unsafe
                        ? DocumentResultCode.UnsafeFile
                        : DocumentResultCode.ScanIndeterminate;
                    return Failure(result, code, scan.Message);
                }
            }

            committedPath = await _fileStorage.CommitCandidateAsync(
                candidate,
                DocumentFileTypes.GetNormalizedExtension(file.OriginalFileName),
                cancellationToken);

            var now = DateTime.UtcNow;
            var document = new Document
            {
                Title = metadata.Title!.Trim(),
                Description = NormalizeOptional(metadata.Description),
                Category = metadata.Category!.Trim(),
                UploaderId = actorUserId,
                ProjectId = metadata.ProjectId,
                TaskId = metadata.TaskId,
                OriginalFileName = Path.GetFileName(file.OriginalFileName.Trim()),
                FilePath = committedPath,
                FileSize = candidate.Length,
                FileType = file.ContentType.Trim(),
                UploadedAtUtc = now,
                UpdatedAtUtc = now,
                IsAvailable = true
            };

            foreach (var tag in NormalizeTags(metadata.Tags))
            {
                document.Tags.Add(new DocumentTag
                {
                    TagText = tag,
                    NormalizedTagText = tag.ToUpperInvariant(),
                    CreatedAtUtc = now
                });
            }

            document.Activities.Add(new DocumentActivity
            {
                ActorUserId = actorUserId,
                ActionType = DocumentActivityType.Upload,
                DocumentTitleSnapshot = document.Title,
                UploaderDisplayNameSnapshot = await _context.Users
                    .AsNoTracking()
                    .Where(user => user.UserId == actorUserId)
                    .Select(user => user.DisplayName)
                    .SingleOrDefaultAsync(cancellationToken) ?? "Unknown user",
                FileType = document.FileType,
                OccurredAtUtc = now,
                Details = "Document uploaded."
            });

            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);
            await NotifyProjectParticipantsAsync(document, actorUserId, cancellationToken);
            return new DocumentUploadResult
            {
                Succeeded = true,
                Code = DocumentResultCode.Success,
                Message = "Document uploaded successfully.",
                DocumentId = document.DocumentId,
                Document = await ToSummaryAsync(document, actorUserId, cancellationToken),
                OriginalFileName = result.OriginalFileName
            };
        }
        catch (FileTooLargeException exception)
        {
            if (candidate != null)
            {
                await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            }

            return Failure(result, DocumentResultCode.FileTooLarge, exception.Message);
        }
        catch (FileStorageException exception)
        {
            if (candidate != null)
            {
                await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            }

            if (committedPath != null)
            {
                await _fileStorage.DeleteAsync(committedPath, CancellationToken.None);
            }

            return Failure(result, DocumentResultCode.StorageFailure, "The document could not be stored.");
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception, "Could not persist document metadata for {FileName}.", file.OriginalFileName);
            _context.ChangeTracker.Clear();
            if (candidate != null)
            {
                await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            }

            if (committedPath != null)
            {
                await _fileStorage.DeleteAsync(committedPath, CancellationToken.None);
            }

            return Failure(result, DocumentResultCode.PersistenceFailure, "The document metadata could not be saved.");
        }
        catch (OperationCanceledException)
        {
            if (candidate != null)
            {
                await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            }

            if (committedPath != null)
            {
                await _fileStorage.DeleteAsync(committedPath, CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected document upload failure for {FileName}.", file.OriginalFileName);
            _context.ChangeTracker.Clear();
            if (candidate != null)
            {
                await _fileStorage.CleanupCandidateAsync(candidate, CancellationToken.None);
            }

            if (committedPath != null)
            {
                await _fileStorage.DeleteAsync(committedPath, CancellationToken.None);
            }

            return Failure(result, DocumentResultCode.StorageFailure, "The document upload could not be completed.");
        }
    }

    private async Task<DocumentResultCode?> ValidateUploadAsync(
        int actorUserId,
        DocumentMetadataInput metadata,
        DocumentUploadItem file,
        CancellationToken cancellationToken)
    {
        if (!await _context.Users.AnyAsync(user => user.UserId == actorUserId, cancellationToken))
        {
            return DocumentResultCode.Unauthorized;
        }

        if (string.IsNullOrWhiteSpace(metadata.Title)
            || metadata.Title.Trim().Length > 255
            || string.IsNullOrWhiteSpace(metadata.Category)
            || !DocumentCategories.IsValid(metadata.Category))
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Trim().Length > 2000)
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (metadata.Tags != null && NormalizeTags(metadata.Tags).Count != metadata.Tags.Count)
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (string.IsNullOrWhiteSpace(file.OriginalFileName)
            || file.OriginalFileName.Trim().Length > 255
            || string.IsNullOrWhiteSpace(file.ContentType)
            || file.ContentType.Trim().Length > 255
            || file.Length <= 0)
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (file.Length > DocumentStorageLimits.MaxFileSizeBytes)
        {
            return DocumentResultCode.FileTooLarge;
        }

        if (!DocumentFileTypes.IsSupported(file.OriginalFileName, file.ContentType))
        {
            return DocumentResultCode.UnsupportedFileType;
        }

        return await ValidateAssociationAsync(actorUserId, metadata.ProjectId, metadata.TaskId, cancellationToken);
    }

    private async Task<(User? Actor, Document? Document)> LoadMutationDocumentAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return (null, null);
        }

        var document = await _context.Documents
            .Where(item => item.DocumentId == documentId && item.IsAvailable)
            .Include(item => item.Uploader)
            .Include(item => item.Project)
            .Include(item => item.Tags)
            .Include(item => item.Activities)
            .SingleOrDefaultAsync(cancellationToken);
        return (actor, document);
    }

    private static DocumentResultCode? ValidateMetadataValues(
        string? title,
        string? description,
        string? category,
        IReadOnlyCollection<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(title)
            || title.Trim().Length > 255
            || string.IsNullOrWhiteSpace(category)
            || !DocumentCategories.IsValid(category))
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length > 2000)
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        if (tags != null && NormalizeTags(tags).Count != tags.Count)
        {
            return DocumentResultCode.MissingRequiredMetadata;
        }

        return null;
    }

    private async Task<DocumentResultCode?> ValidateAssociationAsync(
        int actorUserId,
        int? projectId,
        int? taskId,
        CancellationToken cancellationToken)
    {
        var actor = await _context.Users.AsNoTracking().SingleAsync(user => user.UserId == actorUserId, cancellationToken);
        if (taskId.HasValue)
        {
            var task = await _context.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.TaskId == taskId.Value, cancellationToken);
            if (task == null || !task.ProjectId.HasValue || (projectId.HasValue && projectId != task.ProjectId))
            {
                return DocumentResultCode.InvalidAssociation;
            }

            projectId = task.ProjectId;
        }

        if (!projectId.HasValue)
        {
            return null;
        }

        var projectExists = await _context.Projects.AnyAsync(project => project.ProjectId == projectId.Value, cancellationToken);
        if (!projectExists)
        {
            return DocumentResultCode.InvalidAssociation;
        }

        if (actor.Role == UserRole.Administrator || actor.Role == UserRole.ProjectManager)
        {
            if (actor.Role == UserRole.ProjectManager
                && !await _context.Projects.AnyAsync(project => project.ProjectId == projectId && project.ProjectManagerId == actorUserId, cancellationToken))
            {
                return DocumentResultCode.Unauthorized;
            }

            return null;
        }

        var member = await _context.ProjectMembers.AnyAsync(
            membership => membership.ProjectId == projectId && membership.UserId == actorUserId,
            cancellationToken);
        return member ? null : DocumentResultCode.Unauthorized;
    }

    private static DocumentMetadataInput MergeMetadata(DocumentMetadataInput shared, DocumentMetadataInput? overrideMetadata) => new()
    {
        Title = overrideMetadata?.Title ?? shared.Title,
        Description = overrideMetadata?.Description ?? shared.Description,
        Category = overrideMetadata?.Category ?? shared.Category,
        Tags = overrideMetadata?.Tags ?? shared.Tags,
        ProjectId = overrideMetadata?.ProjectId ?? shared.ProjectId,
        TaskId = overrideMetadata?.TaskId ?? shared.TaskId
    };

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        return (tags ?? Array.Empty<string>())
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0 && tag.Length <= 100)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ValidationMessage(DocumentResultCode code) => code switch
    {
        DocumentResultCode.MissingRequiredMetadata => "A trimmed title, category, and valid metadata are required.",
        DocumentResultCode.UnsupportedFileType => "The file extension and content type are not supported together.",
        DocumentResultCode.FileTooLarge => "The file exceeds the 25 MiB limit.",
        DocumentResultCode.InvalidAssociation => "The project or task association is invalid.",
        DocumentResultCode.Unauthorized => "You are not authorized to upload to this association.",
        _ => "The upload could not be validated."
    };

    private static DocumentUploadResult Failure(DocumentUploadResult result, DocumentResultCode code, string message) => new()
    {
        Succeeded = false,
        Code = code,
        Message = message,
        OriginalFileName = result.OriginalFileName
    };

    private static DocumentMutationResult MutationFailure(DocumentResultCode code, string message) => new()
    {
        Succeeded = false,
        Code = code,
        Message = message
    };

    private async Task<DocumentSummary> ToSummaryAsync(
        Document document,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        await _context.Entry(document).Reference(item => item.Uploader).LoadAsync(cancellationToken);
        if (document.ProjectId.HasValue)
        {
            await _context.Entry(document).Reference(item => item.Project).LoadAsync(cancellationToken);
        }

        var actor = await _context.Users.AsNoTracking()
            .SingleAsync(user => user.UserId == actorUserId, cancellationToken);
        return ToSummary(document, actor);
    }

    private static DocumentSummary ToSummary(Document document, User actor)
    {
        var canManage = CanManage(actor, document);

        return new DocumentSummary
        {
            DocumentId = document.DocumentId,
            Title = document.Title,
            Description = document.Description,
            Category = document.Category,
            UploaderId = document.UploaderId,
            UploaderName = document.Uploader?.DisplayName ?? string.Empty,
            ProjectId = document.ProjectId,
            ProjectName = document.Project?.Name,
            TaskId = document.TaskId,
            OriginalFileName = document.OriginalFileName,
            FileSize = document.FileSize,
            FileType = document.FileType,
            UploadedAtUtc = document.UploadedAtUtc,
            UpdatedAtUtc = document.UpdatedAtUtc,
            Tags = document.Tags.Select(tag => tag.TagText).ToArray(),
            CanEdit = canManage,
            CanReplace = canManage,
            CanDelete = canManage,
            CanShare = CanShare(actor, document)
        };
    }

    private static bool Matches(Document document, DocumentQueryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Category)
            && !document.Category.Equals(options.Category.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.ProjectId.HasValue && document.ProjectId != options.ProjectId)
        {
            return false;
        }

        if (options.TaskId.HasValue && document.TaskId != options.TaskId)
        {
            return false;
        }

        if (options.DateFromUtc.HasValue && document.UploadedAtUtc < options.DateFromUtc.Value)
        {
            return false;
        }

        if (options.DateToUtc.HasValue && document.UploadedAtUtc > options.DateToUtc.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            return true;
        }

        var search = options.SearchTerm.Trim();
        return document.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (document.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || document.OriginalFileName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || document.Uploader.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (document.Project?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || document.Tags.Any(tag => tag.TagText.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Document> ApplySort(
        IEnumerable<Document> documents,
        DocumentSortField sortBy,
        bool descending)
    {
        Func<Document, object> keySelector = sortBy switch
        {
            DocumentSortField.Title => document => document.Title,
            DocumentSortField.Category => document => document.Category,
            DocumentSortField.FileSize => document => document.FileSize,
            _ => document => document.UploadedAtUtc
        };

        return descending
            ? documents.OrderByDescending(keySelector).ThenByDescending(document => document.DocumentId)
            : documents.OrderBy(keySelector).ThenBy(document => document.DocumentId);
    }

    private static bool CanAccess(User actor, Document document)
    {
        if (!document.IsAvailable)
        {
            return false;
        }

        if (actor.Role == UserRole.Administrator || document.UploaderId == actor.UserId)
        {
            return true;
        }

        if (actor.Role == UserRole.TeamLead
            && document.Uploader.Department != null
            && document.Uploader.Department.Equals(actor.Department, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (document.Project != null
            && (document.Project.ProjectManagerId == actor.UserId
                || document.Project.ProjectMembers.Any(member => member.UserId == actor.UserId)))
        {
            return true;
        }

        return document.Shares.Any(share => share.RevokedAtUtc == null
            && ((share.TargetUserId.HasValue && share.TargetUserId == actor.UserId)
                || (share.TargetDepartment != null
                    && actor.Department != null
                    && share.TargetDepartment.Equals(actor.Department, StringComparison.OrdinalIgnoreCase))));
    }

    private static bool HasExplicitShare(User actor, Document document)
    {
        return document.Shares.Any(share => share.RevokedAtUtc == null
            && ((share.TargetUserId.HasValue && share.TargetUserId == actor.UserId)
                || (share.TargetDepartment != null
                    && actor.Department != null
                    && share.TargetDepartment.Equals(actor.Department, StringComparison.OrdinalIgnoreCase))));
    }

    private static bool CanManage(User actor, Document document)
    {
        if (actor.Role == UserRole.Administrator || document.UploaderId == actor.UserId)
        {
            return true;
        }

        return document.Project?.ProjectManagerId == actor.UserId;
    }

    private static bool CanShare(User actor, Document document)
    {
        return CanManage(actor, document);
    }

    private static string SanitizeFileName(string fileName, int documentId)
    {
        var safeName = Path.GetFileName(fileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(safeName) ? $"document-{documentId}" : safeName;
    }

    private async Task<(User? Actor, List<Document> Documents)> LoadAvailableDocumentsAsync(
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserId == actorUserId, cancellationToken);
        if (actor == null)
        {
            return (null, new List<Document>());
        }

        var documents = await _context.Documents
            .AsNoTracking()
            .Where(document => document.IsAvailable)
            .Include(document => document.Uploader)
            .Include(document => document.Project)
                .ThenInclude(project => project.ProjectMembers)
            .Include(document => document.Tags)
            .Include(document => document.Shares)
            .ToListAsync(cancellationToken);
        return (actor, documents);
    }

    private async Task NotifyProjectParticipantsAsync(
        Document document,
        int uploaderId,
        CancellationToken cancellationToken)
    {
        if (!document.ProjectId.HasValue)
        {
            return;
        }

        try
        {
            var project = await _context.Projects
                .AsNoTracking()
                .Include(item => item.ProjectMembers)
                .SingleOrDefaultAsync(item => item.ProjectId == document.ProjectId.Value, cancellationToken);
            if (project == null)
            {
                return;
            }

            var recipientIds = project.ProjectMembers
                .Select(member => member.UserId)
                .Append(project.ProjectManagerId)
                .Where(userId => userId != uploaderId)
                .Distinct()
                .ToList();

            foreach (var recipientId in recipientIds)
            {
                await _notificationService.CreateNotificationAsync(new Notification
                {
                    UserId = recipientId,
                    Title = "New Project Document",
                    Message = $"{document.Title} was added to project {project.Name}.",
                    Type = NotificationType.DocumentAddedToProject,
                    Priority = NotificationPriority.Informational
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not notify project participants about document {DocumentId}.", document.DocumentId);
        }
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
