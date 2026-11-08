using Azure.Storage.Blobs;
using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public sealed class CuratedStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsolatedCuratedStorageRejectsUploadsAndDeletes(bool explicitClient)
    {
        var client = new BlobServiceClient(new Uri("https://storage.example.com"));
        var storage = new BlobStorageService(client, "files",
            curatedBlobServiceClient: explicitClient ? client : null, isolateCurated: true);
        using var content = new MemoryStream(new byte[] { 1 });
        var upload = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.UploadAsync(CollectionScope.CuratedTools, Guid.NewGuid(), "review.txt", content, "text/plain"));
        Assert.Contains("read-only", upload.Message);
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.DeleteAsync(CollectionScope.CuratedTools, "curated-tools/review.txt"));
        Assert.Contains("read-only", delete.Message);
    }
}
