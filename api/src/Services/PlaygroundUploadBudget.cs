using System.Globalization;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WorkflowGallery.Api.Services;

public sealed class PlaygroundStorageLimitException()
    : Exception("The Playground upload budget is exhausted or busy. An operator must review and reset the sandbox.");

public static class PlaygroundUploadBudget
{
    // Reservations are never refunded, including uncertain/cancelled uploads. Reset only
    // while writes are disabled and after clearing the entire dedicated blob container.
    public static async Task ReserveAsync(BlobClient counter, long bytes, CancellationToken cancellationToken)
    {
        if (bytes <= 0 || bytes > PlaygroundWriteGuard.MaxStoredAttachmentBytes)
            throw new PlaygroundStorageLimitException();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            long reserved = 0;
            ETag? version = null;
            try
            {
                var current = await counter.DownloadContentAsync(cancellationToken);
                if (!long.TryParse(current.Value.Content.ToString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out reserved) ||
                    reserved < 0 || reserved > PlaygroundWriteGuard.MaxStoredAttachmentBytes)
                    throw new InvalidOperationException("The Playground upload budget is invalid; uploads are disabled.");
                version = current.Value.Details.ETag;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // First reservation creates the counter atomically.
            }

            if (bytes > PlaygroundWriteGuard.MaxStoredAttachmentBytes - reserved)
                throw new PlaygroundStorageLimitException();
            try
            {
                await counter.UploadAsync(
                    BinaryData.FromString((reserved + bytes).ToString(CultureInfo.InvariantCulture)),
                    new BlobUploadOptions
                    {
                        Conditions = version is { } etag
                            ? new BlobRequestConditions { IfMatch = etag }
                            : new BlobRequestConditions { IfNoneMatch = ETag.All },
                    },
                    cancellationToken);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Another worker reserved bytes; read the new counter before retrying.
            }
        }
        throw new PlaygroundStorageLimitException();
    }
}
