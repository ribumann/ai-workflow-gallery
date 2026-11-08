using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WorkflowGallery.Api.Services;

/// <summary>
/// Wraps <see cref="BlobContainerClient"/> for the single private container holding task file
/// uploads. Blobs are organized by task ID so all files for one task live under a common
/// prefix, and deleting a task's blob(s) never risks
/// touching another task's file.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Uploads <paramref name="content"/> as the given task's attached file, returning
    /// the blob path stored on the Task row.</summary>
    Task<string> UploadAsync(
        string collectionId,
        Guid taskId,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a readable stream for a previously uploaded blob, or null if it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default);

    /// <summary>Deletes a blob if present; a no-op (not an error) if it is already gone.</summary>
    Task DeleteAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobServiceClient? _playgroundBlobServiceClient;
    private readonly string _containerName;
    private readonly string? _playgroundContainerName;
    private readonly BlobServiceClient? _curatedBlobServiceClient;
    private readonly bool _isolateCurated;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? playgroundContainerName = null,
        BlobServiceClient? playgroundBlobServiceClient = null,
        BlobServiceClient? curatedBlobServiceClient = null,
        bool isolateCurated = false)
    {
        _blobServiceClient = blobServiceClient;
        _playgroundBlobServiceClient = playgroundBlobServiceClient;
        _containerName = containerName;
        _playgroundContainerName = string.IsNullOrWhiteSpace(playgroundContainerName) ? null : playgroundContainerName;
        _curatedBlobServiceClient = curatedBlobServiceClient;
        _isolateCurated = isolateCurated || curatedBlobServiceClient is not null;
    }

    public async Task<string> UploadAsync(
        string collectionId,
        Guid taskId,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        EnsureWrite(collectionId);
        var containerClient = ContainerFor(collectionId);
        if (collectionId != CollectionScope.Playground && !(collectionId == CollectionScope.CuratedTools && _isolateCurated))
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // fileName originates from the client-supplied multipart Content-Disposition header and is
        // otherwise untrusted. Strip any directory component and reject traversal sequences before
        // it becomes part of the blob path, so a crafted name like "../otherTaskId/file.md"
        // cannot escape this task's blob prefix and overwrite another task's file.
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Contains(".."))
        {
            throw new ArgumentException("File name is invalid.", nameof(fileName));
        }

        // Stage each upload separately: a rejected database write must not overwrite the
        // previously saved attachment, even when the new file has the same name.
        var blobPath = $"{CollectionPrefix(collectionId)}{taskId}/{Guid.NewGuid():N}/{safeFileName}";
        var blobClient = containerClient.GetBlobClient(blobPath);

        if (collectionId == CollectionScope.Playground)
        {
            if (!content.CanSeek)
                throw new InvalidOperationException("Playground uploads require a known stream length.");
            await PlaygroundUploadBudget.ReserveAsync(
                containerClient.GetBlobClient("playground/.upload-budget"),
                content.Length - content.Position, cancellationToken);
        }

        // BlobClient.UploadAsync(Stream, BlobUploadOptions, ...) always overwrites an existing blob
        // at the same path (unlike the bool-`overwrite` overload, which defaults to false).
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        return blobPath;
    }

    public async Task<Stream?> OpenReadAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default)
    {
        EnsurePath(collectionId, blobPath);
        var blobClient = ContainerFor(collectionId).GetBlobClient(blobPath);
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default)
    {
        EnsureWrite(collectionId);
        EnsurePath(collectionId, blobPath);
        var blobClient = ContainerFor(collectionId).GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private BlobContainerClient ContainerFor(string collectionId)
    {
        if (!CollectionScope.IsValid(collectionId))
            throw new ArgumentException("A valid collection is required for file storage.", nameof(collectionId));
        if (collectionId == CollectionScope.CuratedTools && _isolateCurated)
            return (_curatedBlobServiceClient ??
                throw new InvalidOperationException("Dedicated curated attachment storage is not provisioned."))
                .GetBlobContainerClient("curated-files");
        if (collectionId == CollectionScope.Playground && _playgroundContainerName is null)
            throw new InvalidOperationException("Playground attachment storage is not provisioned.");
        var client = collectionId == CollectionScope.Playground
            ? _playgroundBlobServiceClient ?? _blobServiceClient
            : _blobServiceClient;
        return client.GetBlobContainerClient(
            collectionId == CollectionScope.Playground ? _playgroundContainerName! : _containerName);
    }

    private string CollectionPrefix(string collectionId) =>
        collectionId == CollectionScope.Playground ? "playground/" :
        collectionId == CollectionScope.CuratedTools && _isolateCurated ? "curated-tools/" : "";

    private void EnsureWrite(string collectionId)
    {
        if (_isolateCurated && collectionId == CollectionScope.CuratedTools)
            throw new InvalidOperationException("Curated files are read-only; publish them through a reviewed import package.");
    }

    private void EnsurePath(string collectionId, string blobPath)
    {
        if (!CollectionScope.IsValid(collectionId) ||
            collectionId == CollectionScope.Playground && !blobPath.StartsWith("playground/", StringComparison.Ordinal) ||
            _isolateCurated && collectionId == CollectionScope.CuratedTools && !blobPath.StartsWith("curated-tools/", StringComparison.Ordinal) ||
            collectionId != CollectionScope.Playground && blobPath.StartsWith("playground/", StringComparison.Ordinal) ||
            collectionId != CollectionScope.CuratedTools && blobPath.StartsWith("curated-tools/", StringComparison.Ordinal) ||
            blobPath.Contains("..") || blobPath.Contains('\\') || blobPath.StartsWith('/'))
            throw new InvalidOperationException("The attachment path is outside the selected collection.");
    }
}
