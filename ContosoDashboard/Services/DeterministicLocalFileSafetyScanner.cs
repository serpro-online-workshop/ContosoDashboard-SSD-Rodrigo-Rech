using System.Text;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed class DeterministicLocalFileSafetyScanner : IFileSafetyScanner
{
    private const int PrefixLength = 4096;

    public async Task<FileSafetyScanResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!DocumentFileTypes.IsSupported(fileName, contentType))
            {
                return new FileSafetyScanResult(FileSafetyStatus.Unsafe, "The file type is not supported.");
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            var buffer = new byte[PrefixLength];
            var length = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (length == 0)
            {
                return new FileSafetyScanResult(FileSafetyStatus.Unsafe, "The file is empty or malformed.");
            }

            var text = Encoding.UTF8.GetString(buffer, 0, length);
            if (text.Contains("CONTOSO_INDETERMINATE", StringComparison.OrdinalIgnoreCase))
            {
                return new FileSafetyScanResult(FileSafetyStatus.Indeterminate, "The local safety check could not complete.");
            }

            if (text.Contains("CONTOSO_UNSAFE", StringComparison.OrdinalIgnoreCase))
            {
                return new FileSafetyScanResult(FileSafetyStatus.Unsafe, "The local safety check rejected the file.");
            }

            return new FileSafetyScanResult(FileSafetyStatus.Safe, "The deterministic local safety check passed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new FileSafetyScanResult(FileSafetyStatus.Indeterminate, "The local safety check could not complete.");
        }
    }
}
