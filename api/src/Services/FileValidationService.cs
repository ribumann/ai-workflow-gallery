namespace WorkflowGallery.Api.Services;

/// <summary>
/// Validates uploaded task files against the allowed file-type list and maximum size from
/// spec.md → Assumptions (FR-015). Lightweight text formats relevant to the domain are accepted,
/// plus `.zip` bundles so a task can ship a small set of related files as one upload; other
/// binary/executable uploads remain out of scope.
/// </summary>
public interface IFileValidationService
{
    /// <summary>Validates a candidate upload by file name and size, without reading its content.</summary>
    FileValidationResult Validate(string fileName, long sizeBytes);

    /// <summary>Best-effort content-type for a file name, used when serving a download.</summary>
    string GetContentType(string fileName);
}

/// <summary>Result of <see cref="IFileValidationService.Validate"/>: either valid, or invalid with
/// a user-facing explanation (FR-015).</summary>
public record FileValidationResult(bool IsValid, string? ErrorMessage)
{
    public static FileValidationResult Success() => new(true, null);

    public static FileValidationResult Failure(string message) => new(false, message);
}

public class FileValidationService : IFileValidationService
{
    /// <summary>Allowed extensions per spec.md Assumptions: Markdown, plain text, JSON, YAML, ZIP.</summary>
    private static readonly Dictionary<string, string> AllowedExtensionsToContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".txt"] = "text/plain",
        [".json"] = "application/json",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".zip"] = "application/zip",
    };

    /// <summary>~5 MB per spec.md Assumptions.</summary>
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public FileValidationResult Validate(string fileName, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Failure("The uploaded file must have a name.");
        }

        // fileName comes from the client-supplied multipart Content-Disposition header and is
        // otherwise untrusted; reject any directory component or traversal sequence before it can
        // reach BlobStorageService and be used to build a blob path (path traversal, CWE-22).
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            return FileValidationResult.Failure("The uploaded file name must not contain a path.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensionsToContentType.ContainsKey(extension))
        {
            var allowed = string.Join(", ", AllowedExtensionsToContentType.Keys);
            return FileValidationResult.Failure(
                $"File type '{extension}' is not allowed. Allowed types: {allowed}.");
        }

        if (sizeBytes <= 0)
        {
            return FileValidationResult.Failure("The uploaded file is empty.");
        }

        if (sizeBytes > MaxFileSizeBytes)
        {
            return FileValidationResult.Failure(
                $"The uploaded file is too large ({sizeBytes / (1024.0 * 1024.0):F1} MB). Maximum size is 5 MB.");
        }

        return FileValidationResult.Success();
    }

    public string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return AllowedExtensionsToContentType.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
