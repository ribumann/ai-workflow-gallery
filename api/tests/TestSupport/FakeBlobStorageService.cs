using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>No-op <see cref="IBlobStorageService"/> fake: records upload/delete calls without
/// touching real Azure Storage, so Task write-path tests stay fast and offline.</summary>
public class FakeBlobStorageService : IBlobStorageService
{
    public List<string> UploadedFileNames { get; } = new();
    public List<string> UploadedBlobPaths { get; } = new();
    public List<string> UploadedCollectionIds { get; } = new();

    public List<string> DeletedBlobPaths { get; } = new();
    public Exception? DeleteFailure { get; set; }

    public Task<string> UploadAsync(
        string collectionId,
        Guid taskId,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        UploadedFileNames.Add(fileName);
        UploadedCollectionIds.Add(collectionId);
        var prefix = collectionId == CollectionScope.Playground ? "playground/" : "";
        var path = $"{prefix}{taskId}/{Guid.NewGuid():N}/{fileName}";
        UploadedBlobPaths.Add(path);
        return Task.FromResult(path);
    }

    public Task<Stream?> OpenReadAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default)
    {
        DeletedBlobPaths.Add(blobPath);
        if (DeleteFailure is not null) return Task.FromException(DeleteFailure);
        return Task.CompletedTask;
    }
}
