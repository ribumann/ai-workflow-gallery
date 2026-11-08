namespace WorkflowGallery.Api.Services;

/// <summary>Public deployments may omit storage; attachment reads then fail explicitly without network access.</summary>
public sealed class DisabledBlobStorageService : IBlobStorageService
{
    public Task<string> UploadAsync(string collectionId, Guid taskId, string fileName, Stream content, string contentType,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Attachment storage is disabled.");
    public Task<Stream?> OpenReadAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Attachment storage is not configured for this public demo.");
    public Task DeleteAsync(string collectionId, string blobPath, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Attachment storage is disabled.");
}
