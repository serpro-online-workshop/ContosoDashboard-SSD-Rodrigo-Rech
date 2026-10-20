using Microsoft.Extensions.Configuration;

namespace ContosoDashboard.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    private const long DefaultMaxFileSizeBytes = 26_214_400;
    private readonly string _rootPath;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<LocalFileStorageService> logger)
    {
        var configuredRoot = configuration["DocumentStorage:RootPath"] ?? "AppData/uploads";
        _rootPath = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));
        _maxFileSizeBytes = configuration.GetValue<long?>("DocumentStorage:MaxFileSizeBytes")
            ?? DefaultMaxFileSizeBytes;
        _logger = logger;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StorageCandidate> StoreCandidateAsync(
        DocumentUploadItem upload,
        CancellationToken cancellationToken = default)
    {
        if (upload.Length > _maxFileSizeBytes)
        {
            throw new FileTooLargeException(_maxFileSizeBytes);
        }

        var relativePath = $"candidates/{Guid.NewGuid():N}.upload";
        var fullPath = ResolveSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using var source = await upload.OpenReadAsync(cancellationToken);
            await using var destination = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var length = await CopyBoundedAsync(source, destination, cancellationToken);
            return new StorageCandidate(relativePath, length);
        }
        catch (FileTooLargeException)
        {
            TryDelete(fullPath);
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(fullPath);
            throw new FileStorageException("The upload candidate could not be stored.", exception);
        }
    }

    public Task<string> CommitCandidateAsync(
        StorageCandidate candidate,
        string extension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedExtension = NormalizeExtension(extension);
        var candidatePath = ResolveSafePath(candidate.RelativePath);
        var relativePath = $"documents/{Guid.NewGuid():N}{normalizedExtension}";
        var destinationPath = ResolveSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            File.Move(candidatePath, destinationPath);
            return Task.FromResult(relativePath);
        }
        catch (Exception exception)
        {
            throw new FileStorageException("The upload candidate could not be committed.", exception);
        }
    }

    public Task<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The private document file was not found.", relativePath);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(relativePath);
        TryDelete(fullPath);
        return Task.CompletedTask;
    }

    public Task CleanupCandidateAsync(
        StorageCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(ResolveSafePath(candidate.RelativePath));
        return Task.CompletedTask;
    }

    private async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            total += read;
            if (total > _maxFileSizeBytes)
            {
                throw new FileTooLargeException(_maxFileSizeBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        return total;
    }

    private string ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new FileStorageException("The storage identity is invalid.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileStorageException("The storage identity escapes the private root.");
        }

        return fullPath;
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().ToLowerInvariant();
        if (normalized.Length < 2 || normalized.Length > 10 || !normalized.StartsWith('.')
            || normalized.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
        {
            throw new FileStorageException("The file extension is invalid.");
        }

        return normalized;
    }

    private void TryDelete(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove private document candidate {Path}.", fullPath);
        }
    }
}
