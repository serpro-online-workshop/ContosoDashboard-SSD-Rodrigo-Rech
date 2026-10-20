namespace ContosoDashboard.Services;

public sealed record StorageCandidate(string RelativePath, long Length);

public class FileStorageException : Exception
{
    public FileStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class FileTooLargeException : FileStorageException
{
    public FileTooLargeException(long maxBytes)
        : base($"The file exceeds the maximum size of {maxBytes} bytes.")
    {
    }
}

public interface IFileStorageService
{
    Task<StorageCandidate> StoreCandidateAsync(
        DocumentUploadItem upload,
        CancellationToken cancellationToken = default);

    Task<string> CommitCandidateAsync(
        StorageCandidate candidate,
        string extension,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task CleanupCandidateAsync(
        StorageCandidate candidate,
        CancellationToken cancellationToken = default);
}
