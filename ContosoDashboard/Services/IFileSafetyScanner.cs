namespace ContosoDashboard.Services;

public enum FileSafetyStatus
{
    Safe,
    Unsafe,
    Indeterminate
}

public sealed record FileSafetyScanResult(FileSafetyStatus Status, string Message);

public interface IFileSafetyScanner
{
    Task<FileSafetyScanResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
