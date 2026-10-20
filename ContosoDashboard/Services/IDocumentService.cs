using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<DocumentPage> ListVisibleAsync(
        int actorUserId,
        DocumentQueryOptions options,
        CancellationToken cancellationToken = default);

    Task<DocumentSummary?> GetVisibleAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentUploadResult>> UploadAsync(
        int actorUserId,
        DocumentUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> UpdateMetadataAsync(
        int actorUserId,
        int documentId,
        DocumentMetadataInput metadata,
        DateTime? expectedUpdatedAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> ReplaceFileAsync(
        int actorUserId,
        int documentId,
        DocumentReplacementRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> DeleteAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentShareView>> GetSharesAsync(
        int actorUserId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> CreateShareAsync(
        int actorUserId,
        int documentId,
        DocumentShareTarget target,
        CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> RevokeShareAsync(
        int actorUserId,
        int documentShareId,
        CancellationToken cancellationToken = default);

    Task<DocumentFileAccess?> OpenFileAsync(
        int actorUserId,
        int documentId,
        bool preview,
        CancellationToken cancellationToken = default);

    Task<DocumentActivityReport> GetActivityReportAsync(
        int actorUserId,
        DocumentActivityReportOptions options,
        CancellationToken cancellationToken = default);

    Task<int> GetDocumentCountAsync(
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSummary>> GetRecentUploadsAsync(
        int actorUserId,
        int take = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSummary>> GetSharedWithMeAsync(
        int actorUserId,
        int take = 50,
        CancellationToken cancellationToken = default);
}
