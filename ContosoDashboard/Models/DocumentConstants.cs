namespace ContosoDashboard.Models;

public static class DocumentStorageLimits
{
    public const long MaxFileSizeBytes = 26_214_400;
}

public static class DocumentCategories
{
    public const string ProjectDocuments = "Project Documents";
    public const string TeamResources = "Team Resources";
    public const string PersonalFiles = "Personal Files";
    public const string Reports = "Reports";
    public const string Presentations = "Presentations";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ProjectDocuments,
        TeamResources,
        PersonalFiles,
        Reports,
        Presentations,
        Other
    };

    public static bool IsValid(string? category) => category != null && All.Contains(category.Trim());
}

public static class DocumentFileTypes
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ContentTypesByExtension =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" },
            [".doc"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/msword" },
            [".docx"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            [".xls"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/vnd.ms-excel" },
            [".xlsx"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            [".ppt"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/vnd.ms-powerpoint" },
            [".pptx"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
            [".txt"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/plain" },
            [".jpg"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
            [".jpeg"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
            [".png"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png" }
        };

    public static bool IsSupported(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension)
            && ContentTypesByExtension.TryGetValue(extension, out var contentTypes)
            && contentTypes.Contains(contentType.Trim());
    }

    public static string GetNormalizedExtension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant();

    public static bool IsPreviewable(string contentType) => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase);
}

public enum DocumentActivityType
{
    Upload,
    Download,
    Preview,
    Replace,
    Delete,
    Share,
    AccessReview
}
