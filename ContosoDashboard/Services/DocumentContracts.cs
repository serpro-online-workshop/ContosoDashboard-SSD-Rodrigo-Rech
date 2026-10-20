using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed record DocumentActor(int UserId);

public sealed class DocumentMetadataInput
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyCollection<string>? Tags { get; init; }
    public int? ProjectId { get; init; }
    public int? TaskId { get; init; }
}

public sealed class DocumentUploadItem
{
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Length { get; init; }
    public DocumentMetadataInput? MetadataOverride { get; init; }
    public Func<CancellationToken, Task<Stream>> OpenReadAsync { get; init; } = _ => Task.FromResult<Stream>(Stream.Null);
}

public sealed class DocumentUploadRequest
{
    public DocumentMetadataInput SharedMetadata { get; init; } = new();
    public IReadOnlyCollection<DocumentUploadItem> Files { get; init; } = Array.Empty<DocumentUploadItem>();
}

public sealed class DocumentQueryOptions
{
    public string? SearchTerm { get; init; }
    public string? Category { get; init; }
    public int? ProjectId { get; init; }
    public int? TaskId { get; init; }
    public DateTime? DateFromUtc { get; init; }
    public DateTime? DateToUtc { get; init; }
    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedAt;
    public bool Descending { get; init; } = true;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public enum DocumentSortField
{
    Title,
    UploadedAt,
    Category,
    FileSize
}

public sealed class DocumentSummary
{
    public int DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
    public int UploaderId { get; init; }
    public string UploaderName { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public int? TaskId { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string FileType { get; init; } = string.Empty;
    public DateTime UploadedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool CanEdit { get; init; }
    public bool CanReplace { get; init; }
    public bool CanDelete { get; init; }
    public bool CanShare { get; init; }
}

public sealed class DocumentPage
{
    public IReadOnlyList<DocumentSummary> Items { get; init; } = Array.Empty<DocumentSummary>();
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public enum DocumentResultCode
{
    Success,
    MissingRequiredMetadata,
    UnsupportedFileType,
    FileTooLarge,
    InvalidAssociation,
    Unauthorized,
    NotFound,
    UnsafeFile,
    ScanIndeterminate,
    StorageFailure,
    PersistenceFailure,
    ConcurrencyConflict,
    InvalidTarget,
    InvalidDateRange,
    UnsupportedPreview
}

public sealed class DocumentUploadResult
{
    public bool Succeeded { get; init; }
    public DocumentResultCode Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? DocumentId { get; init; }
    public DocumentSummary? Document { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
}

public sealed class DocumentMutationResult
{
    public bool Succeeded { get; init; }
    public DocumentResultCode Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public DocumentSummary? Document { get; init; }
}

public sealed class DocumentReplacementRequest
{
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Length { get; init; }
    public Func<CancellationToken, Task<Stream>> OpenReadAsync { get; init; } = _ => Task.FromResult<Stream>(Stream.Null);
}

public sealed class DocumentShareTarget
{
    public int? TargetUserId { get; init; }
    public string? TargetDepartment { get; init; }
}

public sealed class DocumentShareView
{
    public int DocumentShareId { get; init; }
    public int DocumentId { get; init; }
    public int? TargetUserId { get; init; }
    public string? TargetUserName { get; init; }
    public string? TargetDepartment { get; init; }
    public int GrantedByUserId { get; init; }
    public DateTime GrantedAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public bool IsActive => !RevokedAtUtc.HasValue;
}

public sealed class DocumentActivityReportOptions
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public string? ActionType { get; init; }
    public string? FileType { get; init; }
    public int? UploaderId { get; init; }
}

public sealed class DocumentActivityReport
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int TotalActivities { get; init; }
    public IReadOnlyList<DocumentActivityReportRow> Rows { get; init; } = Array.Empty<DocumentActivityReportRow>();
}

public sealed class DocumentActivityReportRow
{
    public string ActionType { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string Uploader { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class DocumentFileAccess : IAsyncDisposable
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public bool IsPreview { get; init; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
